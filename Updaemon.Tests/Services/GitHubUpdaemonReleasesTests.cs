using System.Net;
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

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string url = request.RequestUri!.ToString();
                if (url == $"{Releases}/latest")
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = new HttpRequestMessage(request.Method, LatestTag) });
                }

                return Task.FromResult(Assets.TryGetValue(url, out string? content)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content), RequestMessage = request }
                    : new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
            }
        }

        private static GitHubUpdaemonReleases Releasing(FakeGitHub github) =>
            new GitHubUpdaemonReleases(new HttpClient(github), new HostPlatform(false, "linux-arm64"));

        [Theory]
        [InlineData("https://github.com/AdamTovatt/updaemon/releases/tag/v1.2.0", "1.2.0")]
        [InlineData("https://github.com/AdamTovatt/updaemon/releases/tag/v1.3", null)]
        [InlineData("https://github.com/AdamTovatt/updaemon/releases/tag/v1.3.0.0", null)]
        [InlineData("https://github.com/AdamTovatt/updaemon/releases", null)]
        [InlineData("https://github.com/AdamTovatt/updaemon/releases/tag/nightly", null)]
        public void ParseTagUrl_AddressAfterTheLatestRedirect_ReadsOnlyAThreeNumberTag(string url, string? expected)
        {
            Assert.Equal(expected, GitHubUpdaemonReleases.ParseTagUrl(new Uri(url))?.ToString());
        }

        [Fact]
        public async Task GetLatestVersionAsync_ReadsWhereTheLatestPageRedirects()
        {
            Assert.Equal(UpdaemonVersion.Of(1, 2, 0), await Releasing(new FakeGitHub()).GetLatestVersionAsync());
        }

        [Fact]
        public async Task DownloadAsync_ReleaseHasThisMachinesBuild_WritesIt()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                FakeGitHub github = new FakeGitHub();
                github.Assets[$"{Releases}/download/v1.2.0/Updaemon-linux-arm64"] = "binary";
                string destination = temp.GetFullPath("updaemon");

                await Releasing(github).DownloadAsync(UpdaemonVersion.Of(1, 2, 0), destination);

                Assert.Equal("binary", File.ReadAllText(destination));
            }
        }

        [Fact]
        public async Task DownloadAsync_ReleaseHasNoBuildForThisMachine_Throws()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                string destination = temp.GetFullPath("updaemon");

                await Assert.ThrowsAsync<HttpRequestException>(() => Releasing(new FakeGitHub()).DownloadAsync(UpdaemonVersion.Of(1, 2, 0), destination));

                Assert.False(File.Exists(destination));
            }
        }
    }
}
