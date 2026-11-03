using System.Net;
using Updaemon.Common.Models;
using Updaemon.Common.Utilities;
using Updaemon.GithubDistributionService.Services;

namespace Updaemon.GithubDistributionService.Tests
{
    /// <summary>How the GitHub API's answers reach the plugin, without the network.</summary>
    public class GithubApiClientTests
    {
        private const string Api = "https://api.github.com/repos/owner/repo";

        /// <summary>Answers each address with its status in <see cref="Statuses"/>, and 404 for any other.</summary>
        private sealed class FakeGitHub : HttpMessageHandler
        {
            public Dictionary<string, HttpStatusCode> Statuses { get; } = new Dictionary<string, HttpStatusCode>();

            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                HttpStatusCode status = Statuses.GetValueOrDefault(request.RequestUri!.ToString(), HttpStatusCode.NotFound);
                return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{\"tag_name\":\"v1.0.0\",\"assets\":[]}") });
            }
        }

        [Fact]
        public async Task GetLatestReleaseAsync_RepositoryWithoutReleases_IsNull()
        {
            FakeGitHub github = new FakeGitHub { Statuses = { [Api] = HttpStatusCode.OK } };

            Assert.Null(await new GithubApiClient(new HttpClient(github)).GetLatestReleaseAsync("owner", "repo", token: null));
        }

        [Fact]
        public async Task GetLatestReleaseAsync_RepositoryNotFoundWithoutAToken_NeedsOne()
        {
            CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(
                () => new GithubApiClient(new HttpClient(new FakeGitHub())).GetLatestReleaseAsync("owner", "repo", token: null));

            Assert.Equal(CredentialProblem.Missing, ex.Need.Problem);
        }

        [Fact]
        public async Task GetLatestReleaseAsync_RepositoryNotFoundWithAToken_SaysTheTokenMayNotReachIt()
        {
            FakeGitHub github = new FakeGitHub();

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new GithubApiClient(new HttpClient(github)).GetLatestReleaseAsync("owner", "repo", token: "tok"));

            Assert.Equal("owner/repo was not found on GitHub: it does not exist, or the githubToken cannot read it.", ex.Message);
            Assert.All(github.Requests, request => Assert.Equal("Bearer tok", request.Headers.Authorization?.ToString()));
        }

        [Fact]
        public async Task GetLatestReleaseAsync_TokenRejected_NeedsANewOne()
        {
            FakeGitHub github = new FakeGitHub { Statuses = { [$"{Api}/releases/latest"] = HttpStatusCode.Unauthorized } };

            CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(
                () => new GithubApiClient(new HttpClient(github)).GetLatestReleaseAsync("owner", "repo", token: "old"));

            Assert.Equal(CredentialProblem.Rejected, ex.Need.Problem);
        }

        [Fact]
        public async Task GetLatestReleaseAsync_WithoutAToken_SendsNoAuthorization()
        {
            FakeGitHub github = new FakeGitHub { Statuses = { [$"{Api}/releases/latest"] = HttpStatusCode.OK } };

            await new GithubApiClient(new HttpClient(github)).GetLatestReleaseAsync("owner", "repo", token: "  ");

            Assert.Null(Assert.Single(github.Requests).Headers.Authorization);
        }

        [Fact]
        public async Task DownloadReleaseAssetAsync_AsksForTheAssetsBytesWithTheToken()
        {
            using TempDirectory temp = new TempDirectory();
            FakeGitHub github = new FakeGitHub { Statuses = { [$"{Api}/releases/assets/7"] = HttpStatusCode.OK } };

            await new GithubApiClient(new HttpClient(github)).DownloadReleaseAssetAsync("owner", "repo", 7, Path.Combine(temp.Path, "asset"), token: "tok");

            HttpRequestMessage request = Assert.Single(github.Requests);
            Assert.Equal("Bearer tok", request.Headers.Authorization?.ToString());
            Assert.Equal("application/octet-stream", request.Headers.Accept.ToString());
        }

        [Fact]
        public async Task DownloadReleaseAssetAsync_TokenRejected_NeedsANewOne()
        {
            using TempDirectory temp = new TempDirectory();
            FakeGitHub github = new FakeGitHub { Statuses = { [$"{Api}/releases/assets/7"] = HttpStatusCode.Unauthorized } };

            CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(
                () => new GithubApiClient(new HttpClient(github)).DownloadReleaseAssetAsync("owner", "repo", 7, Path.Combine(temp.Path, "asset"), token: "old"));

            Assert.Equal(CredentialProblem.Rejected, ex.Need.Problem);
        }

        /// <summary>A directory removed with the test.</summary>
        private sealed class TempDirectory : IDisposable
        {
            public string Path { get; } = Directory.CreateTempSubdirectory("updaemon-github-test").FullName;

            public void Dispose() => Directory.Delete(Path, recursive: true);
        }

        [Fact]
        public async Task GetLatestReleaseAsync_Found_IsTheRelease()
        {
            FakeGitHub github = new FakeGitHub { Statuses = { [$"{Api}/releases/latest"] = HttpStatusCode.OK } };

            Assert.Equal("v1.0.0", (await new GithubApiClient(new HttpClient(github)).GetLatestReleaseAsync("owner", "repo", token: null))!.TagName);
        }
    }
}
