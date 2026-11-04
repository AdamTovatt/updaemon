using System.Net;
using System.Security.Cryptography;
using System.Text;
using Updaemon.Common.Utilities;
using Updaemon.Configuration;
using Updaemon.Services;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    public class GitHubUpdaemonReleasesTests
    {
        private const string Releases = "https://github.com/AdamTovatt/updaemon/releases";

        /// <summary>
        /// GitHub without the network: <c>releases/latest</c> ends at <see cref="LatestTag"/>, as
        /// HttpClient reports a followed redirect, and only <see cref="Assets"/> can be downloaded.
        /// </summary>
        private sealed class FakeGitHub : HttpMessageHandler
        {
            public string LatestTag { get; init; } = $"{Releases}/tag/v1.2.0";

            public Dictionary<string, string> Assets { get; } = new Dictionary<string, string>();

            public List<string> Requested { get; } = new List<string>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string url = request.RequestUri!.ToString();
                Requested.Add(url);
                if (url == $"{Releases}/latest")
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = new HttpRequestMessage(request.Method, LatestTag) });
                }

                // A stream, as a real response body is, not a buffer: HttpContent handles the two differently.
                return Task.FromResult(Assets.TryGetValue(url, out string? content)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(content))), RequestMessage = request }
                    : new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
            }
        }

        private static GitHubUpdaemonReleases Releasing(FakeGitHub github)
        {
            HttpClient client = new HttpClient(github);
            return new GitHubUpdaemonReleases(client, new ReleaseChecksums(client), new HostPlatform(false, "linux-arm64"));
        }

        private const string Build = $"{Releases}/download/v1.2.0/Updaemon-linux-arm64";

        private const string Sums = $"{Releases}/download/v1.2.0/SHA256SUMS";

        private static string Sha256(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        [Theory]
        [InlineData("https://github.com/AdamTovatt/updaemon/releases/tag/v1.2.0", "1.2.0")]
        [InlineData("https://github.com/AdamTovatt/updaemon/releases/tag/v1.3", null)]
        [InlineData("https://github.com/AdamTovatt/updaemon/releases/tag/v1.3.0.0", null)]
        [InlineData("https://github.com/AdamTovatt/updaemon/releases", null)]
        [InlineData("https://github.com/AdamTovatt/updaemon/releases/tag/nightly", null)]
        public async Task GetLatestVersionAsync_AddressAfterTheLatestRedirect_ReadsOnlyAThreeNumberTag(string url, string? expected)
        {
            UpdaemonVersion? latest = await Releasing(new FakeGitHub { LatestTag = url }).GetLatestVersionAsync();

            Assert.Equal(expected, latest?.ToString());
        }

        [Fact]
        public async Task DownloadAsync_ReleaseHasThisMachinesBuild_WritesIt()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                FakeGitHub github = new FakeGitHub();
                github.Assets[Build] = "binary";
                github.Assets[Sums] = $"{Sha256("other")}  Updaemon-osx-arm64\n{Sha256("binary")}  Updaemon-linux-arm64\n";
                string destination = temp.GetFullPath("updaemon");

                await Releasing(github).DownloadAsync(UpdaemonVersion.Of(1, 2, 0), destination);

                Assert.Equal("binary", File.ReadAllText(destination));
            }
        }

        /// <summary>The release's SHA256SUMS (null: it has none), why the build is refused, and whether it was downloaded first.</summary>
        public static TheoryData<string?, string, bool> UnverifiableBuilds => new TheoryData<string?, string, bool>
        {
            { $"{Sha256("tampered")}  Updaemon-linux-arm64\n", "Updaemon-linux-arm64 does not match its SHA-256 in SHA256SUMS; the download is corrupt or was tampered with.", true },
            { null, "Updaemon-linux-arm64 cannot be verified: its release has no SHA256SUMS.", false },
            { $"{Sha256("binary")}  Updaemon-osx-arm64\n", "SHA256SUMS has no '<sha-256>  Updaemon-linux-arm64' line, so Updaemon-linux-arm64 cannot be verified.", false },
        };

        [Theory]
        [MemberData(nameof(UnverifiableBuilds))]
        public async Task DownloadAsync_BuildThatCannotBeVerified_IsRefused(string? sums, string expected, bool downloaded)
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                FakeGitHub github = new FakeGitHub();
                github.Assets[Build] = "binary";
                if (sums != null)
                {
                    github.Assets[Sums] = sums;
                }

                ChecksumException exception = await Assert.ThrowsAsync<ChecksumException>(() => Releasing(github).DownloadAsync(UpdaemonVersion.Of(1, 2, 0), temp.GetFullPath("updaemon")));

                Assert.Equal(expected, exception.Message);
                Assert.Equal(downloaded, github.Requested.Contains(Build));
            }
        }

        [SkippableFact]
        public async Task DownloadAsync_DiskFull_SaysSoNamingThePath()
        {
            // Every write to /dev/full fails as on a full disk.
            Skip.IfNot(File.Exists("/dev/full"), "No /dev/full on this system.");
            FakeGitHub github = new FakeGitHub();
            // Larger than the file's write buffer, so the write fails while copying, as a real build's does.
            string binary = new string('x', 1024 * 1024);
            github.Assets[Build] = binary;
            github.Assets[Sums] = $"{Sha256(binary)}  Updaemon-linux-arm64\n";

            IOException exception = await Assert.ThrowsAsync<IOException>(() => Releasing(github).DownloadAsync(UpdaemonVersion.Of(1, 2, 0), "/dev/full"));

            Assert.Contains("No space left on device", exception.Message);
            Assert.Contains("/dev/full", exception.Message);
        }

        [Fact]
        public async Task DownloadAsync_ReleaseHasNoBuildForThisMachine_Throws()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                string destination = temp.GetFullPath("updaemon");
                FakeGitHub github = new FakeGitHub();
                github.Assets[Sums] = $"{Sha256("binary")}  Updaemon-linux-arm64\n";

                await Assert.ThrowsAsync<HttpRequestException>(() => Releasing(github).DownloadAsync(UpdaemonVersion.Of(1, 2, 0), destination));

                Assert.False(File.Exists(destination));
            }
        }
    }
}
