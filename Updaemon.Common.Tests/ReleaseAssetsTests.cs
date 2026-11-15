using System.Security.Cryptography;
using System.Text;
using Updaemon.Common.Models;
using Updaemon.Common.Utilities;

namespace Updaemon.Common.Tests
{
    /// <summary>Which asset of a release is installed, and how it is verified against the release's SHA256SUMS.</summary>
    public sealed class ReleaseAssetsTests : IDisposable
    {
        private const string Label = "release v1.2.3 of owner/repo";

        private readonly string _targetPath = Path.Combine(Path.GetTempPath(), $"release-assets-{Guid.NewGuid():N}");
        private readonly Dictionary<string, string> _contents = new Dictionary<string, string>();
        private readonly List<string> _downloaded = new List<string>();
        private readonly RecordingPostProcessor _postProcessor = new RecordingPostProcessor();

        private sealed class RecordingPostProcessor : IDownloadPostProcessor
        {
            public List<string> Processed { get; } = new List<string>();

            public Task ProcessAsync(string targetDirectory, CancellationToken cancellationToken = default)
            {
                Processed.Add(targetDirectory);
                return Task.CompletedTask;
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_targetPath))
            {
                Directory.Delete(_targetPath, recursive: true);
            }
        }

        private ReleaseAssetsTests With(string name, string content = "build")
        {
            _contents[name] = content;
            return this;
        }

        private ReleaseAssetsTests WithSums(params (string Name, string Content)[] listed) =>
            With(Sha256Sums.AssetName, SumsOf(listed));

        private static string Sha256(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        private static string SumsOf(params (string Name, string Content)[] listed) =>
            string.Concat(listed.Select(entry => $"{Sha256(entry.Content)}  {entry.Name}\n"));

        private Task<DownloadResult> DownloadAsync(string? pattern = null, string runtimeIdentifier = "linux-arm64")
        {
            ReleaseAssets<string> assets = new ReleaseAssets<string>(
                Label,
                "owner/repo",
                _contents.Keys.ToArray(),
                name => name,
                (name, _) => Task.FromResult(_contents[name]),
                (name, filePath, token) =>
                {
                    _downloaded.Add(name);
                    return File.WriteAllTextAsync(filePath, _contents[name], token);
                },
                _postProcessor);
            return assets.DownloadAsync(pattern, runtimeIdentifier, _targetPath);
        }

        private string[] SavedFiles() => Directory.GetFiles(_targetPath).Select(path => Path.GetFileName(path)!).ToArray();

        [Theory]
        [InlineData("linux-arm64", "app-linux-arm64.zip")]
        [InlineData("linux-arm", "app-linux-arm.tar.gz")]
        [InlineData("osx-arm64", "app-osx-arm64")]
        [InlineData("LINUX-X64", "app-linux-x64.zip")]
        public async Task DownloadAsync_WithoutPattern_TakesTheBuildForTheRuntime(string runtimeIdentifier, string expected)
        {
            With("app-linux-x64.zip").With("app-linux-arm64.zip").With("app-linux-arm.tar.gz").With("app-osx-arm64").With("app-osx-arm64.sha256");

            await DownloadAsync(runtimeIdentifier: runtimeIdentifier);

            Assert.Equal(new[] { expected }, SavedFiles());
        }

        [Fact]
        public async Task DownloadAsync_WithPattern_TakesTheAssetItMatches()
        {
            With("app-linux-arm.zip").With("app-linux-x64.zip").With("app-windows.zip");

            await DownloadAsync("*-LINUX-arm.zip", runtimeIdentifier: "linux-x64");

            Assert.Equal(new[] { "app-linux-arm.zip" }, SavedFiles());
        }

        [Fact]
        public async Task DownloadAsync_NoBuildForTheRuntime_ListsTheAssetsOnePerLine()
        {
            With("MinerUHost-linux-arm64-framework-dependent.zip").With("MinerUHost-linux-arm64-self-contained.zip");

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DownloadAsync());

            Assert.Equal(
                $"No asset in the {Label} is named *-linux-arm64.zip, *-linux-arm64.tar.gz or *-linux-arm64, the build for this machine. Its assets:\n" +
                "  MinerUHost-linux-arm64-framework-dependent.zip\n" +
                "  MinerUHost-linux-arm64-self-contained.zip\n" +
                "Pick one with the remote owner/repo/<pattern>, where <pattern> is an asset name and * matches any text.",
                exception.Message);
        }

        [Theory]
        [InlineData(null, "Several assets in the release v1.2.3 of owner/repo are named *-linux-arm64.zip, *-linux-arm64.tar.gz or *-linux-arm64, the build for this machine.")]
        [InlineData("*-linux-*", "Several assets in the release v1.2.3 of owner/repo match '*-linux-*'.")]
        [InlineData("*.exe", "No asset in the release v1.2.3 of owner/repo matches '*.exe'.")]
        public async Task DownloadAsync_NotExactlyOneMatch_SaysWhichAndListsTheAssets(string? pattern, string problem)
        {
            With("app-linux-arm64.zip").With("app-linux-arm64");

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DownloadAsync(pattern));

            Assert.StartsWith($"{problem} Its assets:\n  app-linux-arm64.zip\n  app-linux-arm64\n", exception.Message);
            Assert.Empty(_downloaded);
        }

        [Fact]
        public async Task DownloadAsync_ReleaseWithoutAssets_SaysSo()
        {
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DownloadAsync());

            Assert.Equal($"The {Label} has no assets.", exception.Message);
        }

        [Fact]
        public async Task DownloadAsync_AssetMatchesItsSum_IsVerifiedThenExtracted()
        {
            With("app-linux-arm64.zip").With("app-linux-x64.zip", "x64").WithSums(("app-linux-arm64.zip", "build"), ("app-linux-x64.zip", "x64"));

            DownloadResult result = await DownloadAsync();

            Assert.True(result.Verified);
            Assert.Equal(new[] { "app-linux-arm64.zip" }, SavedFiles());
            Assert.Equal(new[] { _targetPath }, _postProcessor.Processed);
        }

        [Fact]
        public async Task DownloadAsync_ReleaseWithoutSums_IsExtractedUnverified()
        {
            With("app-linux-arm64");

            DownloadResult result = await DownloadAsync();

            Assert.False(result.Verified);
            Assert.Equal(new[] { _targetPath }, _postProcessor.Processed);
        }

        [Fact]
        public async Task DownloadAsync_PatternMatchingEverything_NeverTakesTheSums()
        {
            With("app").WithSums(("app", "build"));

            DownloadResult result = await DownloadAsync("*");

            Assert.True(result.Verified);
            Assert.Equal(new[] { "app" }, _downloaded);
        }

        /// <summary>The release's SHA256SUMS, why the build "build" is refused against it, and whether it was downloaded first.</summary>
        public static TheoryData<string, string, bool> UnverifiableSums => new TheoryData<string, string, bool>
        {
            { SumsOf(("app-linux-arm64.zip", "tampered")), "app-linux-arm64.zip does not match its SHA-256 in SHA256SUMS; the download is corrupt or was tampered with.", true },
            { SumsOf(("app-osx-arm64.zip", "build")), "SHA256SUMS has no '<sha-256>  app-linux-arm64.zip' line, so app-linux-arm64.zip cannot be verified.", false },
        };

        [Theory]
        [MemberData(nameof(UnverifiableSums))]
        public async Task DownloadAsync_AssetThatCannotBeVerified_FailsBeforeExtracting(string sums, string problem, bool downloaded)
        {
            With("app-linux-arm64.zip").With(Sha256Sums.AssetName, sums);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DownloadAsync());

            Assert.Equal($"The {Label} was not installed: {problem}", exception.Message);
            Assert.Equal(downloaded, _downloaded.Count > 0);
            Assert.Empty(_postProcessor.Processed);
        }
    }
}
