using System.Security.Cryptography;
using System.Text;
using ByteShelfClient;
using ByteShelfClient.InMemory;
using ByteShelfCommon;
using Updaemon.Common;
using Updaemon.Common.Models;
using Updaemon.Common.Utilities;

namespace Updaemon.Distribution.ByteShelfDistribution.Tests
{
    public sealed class ByteShelfDistributionServiceTests : IAsyncLifetime
    {
        private const string Rid = "linux-arm64";

        private readonly string _targetPath = Path.Combine(Path.GetTempPath(), $"byteshelf-distribution-{Guid.NewGuid():N}");
        private readonly InMemoryByteShelfClient _byteShelf = new InMemoryByteShelfClient();
        private readonly IDownloadPostProcessor _postProcessor = Substitute.For<IDownloadPostProcessor>();
        private readonly List<(Uri Server, string Key)> _connections = new List<(Uri Server, string Key)>();

        public async Task InitializeAsync()
        {
            await _byteShelf.CreateShelfAsync("tools");
        }

        public Task DisposeAsync()
        {
            if (Directory.Exists(_targetPath))
            {
                Directory.Delete(_targetPath, recursive: true);
            }

            return Task.CompletedTask;
        }

        private static SecretCollection Secrets(string? key = "bsk_test", string? url = "https://shelf.example")
        {
            Dictionary<string, string> secrets = new Dictionary<string, string>();
            if (key != null)
            {
                secrets[ByteShelfDistributionService.KeySecret] = key;
            }

            if (url != null)
            {
                secrets[ByteShelfDistributionService.UrlSecret] = url;
            }

            return new SecretCollection(secrets);
        }

        private ByteShelfDistributionService Service(IByteShelfClient? byteShelf = null) =>
            new ByteShelfDistributionService(new VersionParser(), _postProcessor, (server, key) =>
            {
                _connections.Add((server, key));
                return byteShelf ?? _byteShelf;
            });

        private async Task<ByteShelfDistributionService> ServiceAsync(SecretCollection? secrets = null, IByteShelfClient? byteShelf = null)
        {
            ByteShelfDistributionService service = Service(byteShelf);
            await service.InitializeAsync(secrets ?? Secrets());
            return service;
        }

        private Task PutAsync(string path, string content) =>
            _byteShelf.Shelf("tools").UploadAsync(path, new MemoryStream(Encoding.UTF8.GetBytes(content)));

        private Task PutSumsAsync(string versionDirectory, string name, string content) =>
            PutAsync($"{versionDirectory}/{Sha256Sums.AssetName}", $"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))}  {name}\n");

        /// <summary>Version 1.2.0 of tools/myapp with a build for this machine and one for another.</summary>
        private async Task PutBuildsAsync()
        {
            await PutAsync("/myapp/1.2.0/myapp-linux-arm64.zip", "arm build");
            await PutAsync("/myapp/1.2.0/myapp-linux-x64.zip", "x64 build");
        }

        private async Task<DownloadResult> DownloadAsync(string remote = "tools/myapp", Version? version = null) =>
            await (await ServiceAsync()).DownloadVersionAsync(remote, version ?? new Version(1, 2, 0), Rid, _targetPath);

        /// <summary>A shelf whose listing of the app is refused with <paramref name="code"/>.</summary>
        private static IByteShelfClient Refusing(string code, IShelfClient? shelf = null)
        {
            shelf ??= Substitute.For<IShelfClient>();
            shelf.ListAsync(Arg.Any<EntryReference?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<DirectoryListing>(new ByteShelfException("refused", code)));
            IByteShelfClient byteShelf = Substitute.For<IByteShelfClient>();
            byteShelf.Shelf("tools").Returns(shelf);
            return byteShelf;
        }

        [Fact]
        public async Task GetLatestVersionAsync_RemoteInNeitherForm_IsRefusedBeforeConnecting()
        {
            ByteShelfDistributionService service = await ServiceAsync();

            await Assert.ThrowsAsync<ArgumentException>(() => service.GetLatestVersionAsync("myapp"));

            Assert.Empty(_connections);
        }

        [Fact]
        public async Task GetLatestVersionAsync_IsTheHighestVersionDirectory()
        {
            await PutAsync("/myapp/1.2.0/myapp-linux-arm64", "build");
            await PutAsync("/myapp/v1.10.0/myapp-linux-arm64", "build");
            await PutAsync("/myapp/1.9.0/myapp-linux-arm64", "build");
            await PutAsync("/myapp/notes.txt", "a file, not a version");
            await _byteShelf.Shelf("tools").CreateDirectoryAsync("/myapp/drafts");

            Version? latest = await (await ServiceAsync()).GetLatestVersionAsync("tools/myapp");

            Assert.Equal(new Version(1, 10, 0), latest);
        }

        [Fact]
        public async Task GetLatestVersionAsync_AppDirectoryWithoutVersions_IsNone()
        {
            await _byteShelf.Shelf("tools").CreateDirectoryAsync("/myapp/drafts");

            Assert.Null(await (await ServiceAsync()).GetLatestVersionAsync("tools/myapp"));
        }

        [Fact]
        public async Task GetLatestVersionAsync_NoAppDirectory_SaysTheAppHasNoVersions()
        {
            ByteShelfDistributionService service = await ServiceAsync();

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetLatestVersionAsync("tools/myapp"));

            Assert.Equal("tools/myapp has no versions: the shelf 'tools' has no directory /myapp.", ex.Message);
        }

        [Fact]
        public async Task GetLatestVersionAsync_NoSuchShelf_SaysTheKeyMayNotSeeIt()
        {
            ByteShelfDistributionService service = await ServiceAsync();

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetLatestVersionAsync("apps/myapp"));

            Assert.Equal(
                "The shelf 'apps' was not found: it does not exist, or the byteShelfKey cannot see it (a shelf key works on its own shelf only). " +
                "Remotes are <shelf>/<app> since ByteShelf 2.0. Set another key with 'updaemon secret-set byteshelf byteShelfKey'.",
                ex.Message);
        }

        [Fact]
        public async Task GetLatestVersionAsync_KeyNotSet_NeedsTheKeyBeforeConnecting()
        {
            ByteShelfDistributionService service = await ServiceAsync(Secrets(key: null));

            CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(() => service.GetLatestVersionAsync("tools/myapp"));

            Assert.Equal("The byteShelfKey is not set: reading the shelf 'tools' needs a shelf key for it.", ex.Message);
            Assert.Equal(ByteShelfDistributionService.KeySecret, ex.Need.Secret);
            Assert.Equal(CredentialProblem.Missing, ex.Need.Problem);
            Assert.Empty(_connections);
        }

        [Fact]
        public async Task InitializeAsync_AgainWithNewSecrets_TheNextCallConnectsWithThem()
        {
            await _byteShelf.Shelf("tools").CreateDirectoryAsync("/myapp");
            ByteShelfDistributionService service = await ServiceAsync(Secrets(key: "bsk_old"));
            await service.GetLatestVersionAsync("tools/myapp");

            await service.InitializeAsync(SecretCollection.FromString("BYTESHELFKEY=bsk_new\nbyteShelfUrl=https://shelf.example/base"));
            await service.GetLatestVersionAsync("tools/myapp");

            Assert.Equal(new[] { (new Uri("https://shelf.example/"), "bsk_old"), (new Uri("https://shelf.example/base/"), "bsk_new") }, _connections);
        }

        [Theory]
        [InlineData(null, "The ByteShelf server's address, byteShelfUrl, is not set.")]
        [InlineData("ftp://shelf.example", "The byteShelfUrl 'ftp://shelf.example' is not an http or https address.")]
        [InlineData("shelf.example", "The byteShelfUrl 'shelf.example' is not an http or https address.")]
        public async Task GetLatestVersionAsync_NoServerAddress_NamesTheCommandThatSetsIt(string? url, string expected)
        {
            ByteShelfDistributionService service = await ServiceAsync(Secrets(url: url));

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetLatestVersionAsync("tools/myapp"));

            Assert.Equal($"{expected} Set it with 'updaemon secret-set byteshelf byteShelfUrl <url>'.", ex.Message);
        }

        private const string Rejected = "ByteShelf rejected the byteShelfKey: it is wrong, or the key was deleted.";

        /// <summary>A code the server refuses the key with, whether the call is a download, and the message.</summary>
        public static TheoryData<string, bool, string> KeyRefusals => new TheoryData<string, bool, string>
        {
            { ErrorCodes.Unauthenticated, false, Rejected },
            { ErrorCodes.InvalidCredentials, false, Rejected },
            { ErrorCodes.PermissionDenied, false, "The byteShelfKey may not read the shelf 'tools': it needs read access." },
            { ErrorCodes.Unauthenticated, true, Rejected },
        };

        // The in-memory client acts as an admin and refuses nothing for lack of access, so the server's
        // refusals of a key come from a stand-in shelf.
        [Theory]
        [MemberData(nameof(KeyRefusals))]
        public async Task KeyRefused_NeedsAnotherKey(string code, bool download, string expected)
        {
            ByteShelfDistributionService service = await ServiceAsync(byteShelf: Refusing(code));

            CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(() => download
                ? service.DownloadVersionAsync("tools/myapp", new Version(1, 2, 0), Rid, _targetPath)
                : service.GetLatestVersionAsync("tools/myapp"));

            Assert.Equal(expected, ex.Message);
            Assert.Equal(ByteShelfDistributionService.KeySecret, ex.Need.Secret);
            Assert.Equal(CredentialProblem.Rejected, ex.Need.Problem);
        }

        [Fact]
        public async Task GetLatestVersionAsync_KeyRefusedWhenAskingWhetherTheShelfExists_NeedsAnotherKey()
        {
            IShelfClient shelf = Substitute.For<IShelfClient>();
            shelf.GetInfoAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException<ShelfInfo>(new ByteShelfException("refused", ErrorCodes.Unauthenticated)));
            ByteShelfDistributionService service = await ServiceAsync(byteShelf: Refusing(ErrorCodes.NotFound, shelf));

            CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(() => service.GetLatestVersionAsync("tools/myapp"));

            Assert.Equal(Rejected, ex.Message);
        }

        [Fact]
        public async Task DownloadVersionAsync_BuildMatchesItsSum_IsVerifiedThenExtracted()
        {
            await PutBuildsAsync();
            await PutSumsAsync("/myapp/1.2.0", "myapp-linux-arm64.zip", "arm build");

            DownloadResult result = await DownloadAsync();

            Assert.True(result.Verified);
            Assert.Equal("arm build", await File.ReadAllTextAsync(Path.Combine(_targetPath, "myapp-linux-arm64.zip")));
            Assert.Single(Directory.GetFiles(_targetPath));
            await _postProcessor.Received().ProcessAsync(_targetPath, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DownloadVersionAsync_WithPattern_TakesTheAssetItNamesAndVerifiesIt()
        {
            await PutBuildsAsync();
            await PutSumsAsync("/myapp/1.2.0", "myapp-linux-x64.zip", "x64 build");

            DownloadResult result = await DownloadAsync("tools/myapp/*-x64.zip");

            Assert.True(result.Verified);
            Assert.Equal(new[] { "myapp-linux-x64.zip" }, Directory.GetFiles(_targetPath).Select(Path.GetFileName));
        }

        [Fact]
        public async Task DownloadVersionAsync_NoBuildForThisMachine_ListsTheAssets()
        {
            await PutAsync("/myapp/1.2.0/myapp-linux-x64.zip", "x64 build");

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DownloadAsync());

            Assert.Equal(
                "No asset in the version 1.2.0 of tools/myapp is named *-linux-arm64.zip, *-linux-arm64.tar.gz or *-linux-arm64, the build for this machine. Its assets:\n" +
                "  myapp-linux-x64.zip\n" +
                "Pick one with the remote tools/myapp/<pattern>, where <pattern> is an asset name and * matches any text.",
                ex.Message);
        }

        [Fact]
        public async Task DownloadVersionAsync_VersionGoneSinceItWasFound_SaysSo()
        {
            await PutBuildsAsync();

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DownloadAsync(version: new Version(1, 3, 0)));

            Assert.Equal("tools/myapp has no version 1.3.0 any more.", ex.Message);
        }

        [Fact]
        public async Task DownloadVersionAsync_TwoDirectoriesForTheVersion_NamesBoth()
        {
            await PutBuildsAsync();
            await PutAsync("/myapp/v1.2.0/myapp-linux-arm64.zip", "another arm build");

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DownloadAsync());

            Assert.Equal("tools/myapp has several directories for version 1.2.0: /myapp/1.2.0, /myapp/v1.2.0. Keep one.", ex.Message);
            Assert.False(Directory.Exists(_targetPath));
        }

        [Fact]
        public void GetServiceInformation_IsVersion2WithTheUrlAndAShelfKey()
        {
            DistributionServiceInformation information = Service().GetServiceInformation();

            Assert.Equal("2.0.0", information.Version);
            Assert.Equal(new[] { "byteShelfUrl", "byteShelfKey" }, information.Secrets.Select(secret => secret.Name));
            Assert.All(information.Secrets, secret => Assert.True(secret.IsRequired));
        }
    }
}
