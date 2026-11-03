using System.Net;
using Updaemon.Common.Models;
using Updaemon.Common.Utilities;
using Updaemon.Configuration;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    public class GitHubPluginReleasesTests
    {
        private const string Api = "https://api.github.com/repos/someone/updaemon-s3";

        /// <summary>
        /// GitHub without the network: <c>releases/latest</c> on github.com ends where <see cref="LatestRedirect"/>
        /// says, as HttpClient reports a followed redirect; the API has the repository when
        /// <see cref="RepositoryExists"/>, its latest release as <see cref="LatestJson"/>, and release
        /// <see cref="ApiTag"/> as <see cref="ReleaseJson"/>. <see cref="Refusal"/>, when set, answers every API call.
        /// </summary>
        private sealed class FakeGitHub : HttpMessageHandler
        {
            public string? LatestRedirect { get; init; }

            public bool RepositoryExists { get; init; } = true;

            public string? LatestJson { get; init; }

            public string ReleaseJson { get; init; } = "{}";

            /// <summary>The only release tag the API knows, as it appears in the API's address.</summary>
            public string ApiTag { get; init; } = "v2.1.0";

            public Func<HttpResponseMessage>? Refusal { get; init; }

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

                if (Refusal != null)
                {
                    return Task.FromResult(Refusal());
                }

                string? json = url switch
                {
                    Api => RepositoryExists ? "{}" : null,
                    $"{Api}/releases/latest" => LatestJson,
                    _ when url == $"{Api}/releases/tags/{ApiTag}" => ReleaseJson,
                    _ => null,
                };
                return Task.FromResult(json == null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json), RequestMessage = request });
            }
        }

        private readonly MockSecretsManager _secrets = new MockSecretsManager();

        private GitHubPluginReleases Releases(FakeGitHub github) =>
            new GitHubPluginReleases(new HttpClient(github), new HostPlatform(false, "linux-arm64"), _secrets);

        private Task SetTokenAsync(string token) => _secrets.SetSecretAsync("github", "githubToken", token);

        private static string ReleaseWith(params string[] assetNames) =>
            "{\"tag_name\":\"v2.1.0\",\"assets\":[" +
            string.Join(",", assetNames.Select((name, index) =>
                $"{{\"name\":\"{name}\",\"url\":\"{Api}/releases/assets/{index}\",\"browser_download_url\":\"https://github.com/someone/updaemon-s3/releases/download/v2.1.0/{name}\"}}")) +
            "]}";

        [Fact]
        public async Task GetLatestTagAsync_NoToken_ReadsTheTagWhereLatestRedirects()
        {
            FakeGitHub github = new FakeGitHub { LatestRedirect = "https://github.com/someone/updaemon-s3/releases/tag/plugin%2F2.1" };

            Assert.Equal("plugin/2.1", await Releases(github).GetLatestTagAsync("someone", "updaemon-s3"));
            Assert.Single(github.Requests);
        }

        [Fact]
        public async Task GetLatestTagAsync_NoTokenAndNoRelease_SaysSo()
        {
            FakeGitHub github = new FakeGitHub { LatestRedirect = "https://github.com/someone/updaemon-s3/releases" };

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Releases(github).GetLatestTagAsync("someone", "updaemon-s3"));

            Assert.Equal("someone/updaemon-s3 has no release on GitHub.", ex.Message);
        }

        [Fact]
        public async Task GetLatestTagAsync_NoTokenAndNotFound_NeedsAToken()
        {
            CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(() => Releases(new FakeGitHub()).GetLatestTagAsync("someone", "updaemon-s3"));

            Assert.Equal(CredentialProblem.Missing, ex.Need.Problem);
            Assert.Equal("githubToken", ex.Need.Secret);
        }

        [Fact]
        public async Task GetLatestTagAsync_WithAToken_ReadsTheApiWithIt()
        {
            FakeGitHub github = new FakeGitHub { LatestJson = ReleaseWith() };

            await SetTokenAsync("tok");

            Assert.Equal("v2.1.0", await Releases(github).GetLatestTagAsync("someone", "updaemon-s3"));
            HttpRequestMessage request = Assert.Single(github.Requests);
            Assert.Equal($"{Api}/releases/latest", request.RequestUri!.ToString());
            Assert.Equal("Bearer tok", request.Headers.Authorization?.ToString());
        }

        [Theory]
        [InlineData(true, "someone/updaemon-s3 has no release on GitHub.")]
        [InlineData(false, "someone/updaemon-s3 was not found on GitHub: it does not exist, or the githubToken cannot read it.")]
        public async Task GetLatestTagAsync_WithATokenAndNoLatestRelease_SaysWhetherTheRepositoryIsThere(bool repositoryExists, string expected)
        {
            FakeGitHub github = new FakeGitHub { RepositoryExists = repositoryExists };

            await SetTokenAsync("tok");

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Releases(github).GetLatestTagAsync("someone", "updaemon-s3"));

            Assert.Equal(expected, ex.Message);
        }

        [Fact]
        public async Task GetLatestTagAsync_TokenRejected_NeedsANewOne()
        {
            FakeGitHub github = new FakeGitHub { Refusal = () => new HttpResponseMessage(HttpStatusCode.Unauthorized) };

            await SetTokenAsync("old");

            CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(() => Releases(github).GetLatestTagAsync("someone", "updaemon-s3"));

            Assert.Equal(CredentialProblem.Rejected, ex.Need.Problem);
        }

        [Fact]
        public async Task GetBuildAsync_NoToken_IsTheDownloadAddressesOfTheBuildAndItsSums()
        {
            FakeGitHub github = new FakeGitHub { ReleaseJson = ReleaseWith("s3-linux-x64", "s3-linux-arm64", "s3-linux-arm64.zip", "SHA256SUMS") };

            PluginDownload download = await Releases(github).GetBuildAsync("someone", "updaemon-s3", "v2.1.0");

            Assert.Equal(
                new PluginDownload(
                    new Uri("https://github.com/someone/updaemon-s3/releases/download/v2.1.0/s3-linux-arm64"),
                    "s3-linux-arm64",
                    new Uri("https://github.com/someone/updaemon-s3/releases/download/v2.1.0/SHA256SUMS"),
                    new GitHubFetch(Token: null)),
                download);
            Assert.Contains("updaemon", github.Requests.Single().Headers.UserAgent.ToString());
        }

        [Fact]
        public async Task GetBuildAsync_WithAToken_IsTheApiAddressesWithTheToken()
        {
            FakeGitHub github = new FakeGitHub { ReleaseJson = ReleaseWith("s3-linux-arm64", "SHA256SUMS") };

            await SetTokenAsync("tok");

            PluginDownload download = await Releases(github).GetBuildAsync("someone", "updaemon-s3", "v2.1.0");

            Assert.Equal(new PluginDownload(new Uri($"{Api}/releases/assets/0"), "s3-linux-arm64", new Uri($"{Api}/releases/assets/1"), new GitHubFetch("tok")), download);
            Assert.Equal("Bearer tok", github.Requests.Single().Headers.Authorization?.ToString());
        }

        [Fact]
        public async Task GetBuildAsync_ReleaseWithoutSums_CannotBeVerified()
        {
            FakeGitHub github = new FakeGitHub { ReleaseJson = ReleaseWith("s3-linux-arm64") };

            ChecksumException ex = await Assert.ThrowsAsync<ChecksumException>(() => Releases(github).GetBuildAsync("someone", "updaemon-s3", "v2.1.0"));

            Assert.Equal("s3-linux-arm64 cannot be verified: its release has no SHA256SUMS.", ex.Message);
        }

        [Fact]
        public async Task GetBuildAsync_TagWithASlash_IsEscapedInTheApiAddress()
        {
            FakeGitHub github = new FakeGitHub { ApiTag = "plugin%2F2.1", ReleaseJson = ReleaseWith("s3-linux-arm64", "SHA256SUMS") };

            PluginDownload download = await Releases(github).GetBuildAsync("someone", "updaemon-s3", "plugin/2.1");

            Assert.Equal("s3-linux-arm64", download.FileName);
        }

        [Fact]
        public async Task GetBuildAsync_NoSuchRelease_SaysSo()
        {
            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Releases(new FakeGitHub()).GetBuildAsync("someone", "updaemon-s3", "v9.9.9"));

            Assert.Equal("someone/updaemon-s3 has no release v9.9.9 on GitHub.", ex.Message);
        }

        [Theory]
        [InlineData("No asset in the release v2.1.0 of someone/updaemon-s3 is named", "s3-linux-x64", "SHA256SUMS")]
        [InlineData("Several assets in the release v2.1.0 of someone/updaemon-s3 are named", "s3-linux-arm64", "s3-full-linux-arm64")]
        public async Task GetBuildAsync_NotExactlyOneBuild_ListsTheAssets(string problem, params string[] assets)
        {
            FakeGitHub github = new FakeGitHub { ReleaseJson = ReleaseWith(assets) };

            ReleaseBuildException ex = await Assert.ThrowsAsync<ReleaseBuildException>(() => Releases(github).GetBuildAsync("someone", "updaemon-s3", "v2.1.0"));

            Assert.Equal(
                $"{problem} *-linux-arm64, the plugin build for this machine. Its assets:\n" + string.Join("\n", assets.Select(name => $"  {name}")),
                ex.Message);
        }
    }
}
