using System.Net;
using Updaemon.Configuration;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Tests.Services
{
    public class GitHubPluginReleasesTests
    {
        /// <summary>
        /// GitHub without the network: <c>releases/latest</c> ends where <see cref="LatestRedirect"/>
        /// says, as HttpClient reports a followed redirect, and the API answers with <see cref="ReleaseJson"/>.
        /// </summary>
        private sealed class FakeGitHub : HttpMessageHandler
        {
            public string? LatestRedirect { get; init; }

            public string ReleaseJson { get; init; } = "{}";

            /// <summary>The only release tag the API knows, as it appears in the API's address.</summary>
            public string ApiTag { get; init; } = "v2.1.0";

            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                string url = request.RequestUri!.ToString();
                if (url == "https://github.com/someone/updaemon-s3/releases/latest")
                {
                    return Task.FromResult(LatestRedirect == null
                        ? new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request }
                        : new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = new HttpRequestMessage(request.Method, LatestRedirect) });
                }

                return Task.FromResult(url == $"https://api.github.com/repos/someone/updaemon-s3/releases/tags/{ApiTag}"
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReleaseJson), RequestMessage = request }
                    : new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
            }
        }

        private static GitHubPluginReleases Releases(FakeGitHub github) =>
            new GitHubPluginReleases(new HttpClient(github), new HostPlatform(false, "linux-arm64"));

        private static string ReleaseWith(params string[] assetNames) =>
            "{\"tag_name\":\"v2.1.0\",\"assets\":[" +
            string.Join(",", assetNames.Select(name => $"{{\"name\":\"{name}\",\"browser_download_url\":\"https://github.com/someone/updaemon-s3/releases/download/v2.1.0/{name}\"}}")) +
            "]}";

        [Fact]
        public async Task GetLatestTagAsync_ReadsTheTagWhereLatestRedirects()
        {
            FakeGitHub github = new FakeGitHub { LatestRedirect = "https://github.com/someone/updaemon-s3/releases/tag/plugin%2F2.1" };

            Assert.Equal("plugin/2.1", await Releases(github).GetLatestTagAsync("someone", "updaemon-s3"));
        }

        [Theory]
        [InlineData("https://github.com/someone/updaemon-s3/releases", "someone/updaemon-s3 has no release on GitHub.")]
        [InlineData(null, "someone/updaemon-s3 was not found on GitHub.")]
        public async Task GetLatestTagAsync_NoRelease_SaysSo(string? redirect, string expected)
        {
            FakeGitHub github = new FakeGitHub { LatestRedirect = redirect };

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Releases(github).GetLatestTagAsync("someone", "updaemon-s3"));

            Assert.Equal(expected, ex.Message);
        }

        [Fact]
        public async Task GetBuildUrlAsync_OneBuildForThisMachine_IsItsDownloadAddress()
        {
            FakeGitHub github = new FakeGitHub { ReleaseJson = ReleaseWith("s3-linux-x64", "s3-linux-arm64", "s3-linux-arm64.zip", "SHA256SUMS") };

            string url = await Releases(github).GetBuildUrlAsync("someone", "updaemon-s3", "v2.1.0");

            Assert.Equal("https://github.com/someone/updaemon-s3/releases/download/v2.1.0/s3-linux-arm64", url);
            Assert.Contains("updaemon", github.Requests.Single().Headers.UserAgent.ToString());
        }

        [Fact]
        public async Task GetBuildUrlAsync_TagWithASlash_IsEscapedInTheApiAddress()
        {
            FakeGitHub github = new FakeGitHub { ApiTag = "plugin%2F2.1", ReleaseJson = ReleaseWith("s3-linux-arm64") };

            string url = await Releases(github).GetBuildUrlAsync("someone", "updaemon-s3", "plugin/2.1");

            Assert.EndsWith("/s3-linux-arm64", url);
        }

        [Fact]
        public async Task GetBuildUrlAsync_NoSuchRelease_FailsAsAnHttpError()
        {
            HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
                () => Releases(new FakeGitHub()).GetBuildUrlAsync("someone", "updaemon-s3", "v9.9.9"));

            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Theory]
        [InlineData("No asset in the release v2.1.0 of someone/updaemon-s3 is named", "s3-linux-x64", "SHA256SUMS")]
        [InlineData("Several assets in the release v2.1.0 of someone/updaemon-s3 are named", "s3-linux-arm64", "s3-full-linux-arm64")]
        public async Task GetBuildUrlAsync_NotExactlyOneBuild_ListsTheAssets(string problem, params string[] assets)
        {
            FakeGitHub github = new FakeGitHub { ReleaseJson = ReleaseWith(assets) };

            ReleaseBuildException ex = await Assert.ThrowsAsync<ReleaseBuildException>(() => Releases(github).GetBuildUrlAsync("someone", "updaemon-s3", "v2.1.0"));

            Assert.Equal(
                $"{problem} *-linux-arm64, the plugin build for this machine. Its assets:\n" + string.Join("\n", assets.Select(name => $"  {name}")),
                ex.Message);
        }
    }
}
