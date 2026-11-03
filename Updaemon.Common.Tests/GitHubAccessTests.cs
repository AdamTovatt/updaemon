using System.Net;
using Updaemon.Common.Models;
using Updaemon.Common.Utilities;

namespace Updaemon.Common.Tests
{
    public class GitHubAccessTests
    {
        private static HttpResponseMessage Response(HttpStatusCode status, string? remaining = null, string? reset = null)
        {
            HttpResponseMessage response = new HttpResponseMessage(status);
            if (remaining != null)
            {
                response.Headers.Add("x-ratelimit-remaining", remaining);
            }

            if (reset != null)
            {
                response.Headers.Add("x-ratelimit-reset", reset);
            }

            return response;
        }

        [Fact]
        public void EnsureSuccess_Unauthorized_NeedsANewToken()
        {
            CredentialNeededException ex = Assert.Throws<CredentialNeededException>(() => GitHubAccess.EnsureSuccess(Response(HttpStatusCode.Unauthorized)));

            Assert.Equal(("githubToken", CredentialProblem.Rejected), (ex.Need.Secret, ex.Need.Problem));
            Assert.Equal("GitHub rejected the githubToken: it is wrong, expired or revoked.", ex.Message);
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        public void EnsureSuccess_RateLimitUsedUp_SaysWhenItResets(HttpStatusCode status)
        {
            // 1700000000 is 2023-11-14 22:13:20 UTC.
            CredentialNeededException ex = Assert.Throws<CredentialNeededException>(() => GitHubAccess.EnsureSuccess(Response(status, remaining: "0", reset: "1700000000")));

            Assert.Equal(CredentialProblem.RateLimited, ex.Need.Problem);
            Assert.Equal("GitHub's rate limit is used up until 22:13 UTC.", ex.Message);
        }

        [Fact]
        public void EnsureSuccess_RateLimitWithoutAReset_SaysSoWithoutATime()
        {
            CredentialNeededException ex = Assert.Throws<CredentialNeededException>(() => GitHubAccess.EnsureSuccess(Response(HttpStatusCode.Forbidden, remaining: "0")));

            Assert.Equal("GitHub's rate limit is used up.", ex.Message);
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden, "12")]
        [InlineData(HttpStatusCode.Forbidden, null)]
        [InlineData(HttpStatusCode.NotFound, null)]
        [InlineData(HttpStatusCode.InternalServerError, null)]
        public void EnsureSuccess_AnyOtherFailure_IsAnHttpError(HttpStatusCode status, string? remaining)
        {
            HttpRequestException ex = Assert.Throws<HttpRequestException>(() => GitHubAccess.EnsureSuccess(Response(status, remaining)));

            Assert.Equal(status, ex.StatusCode);
        }

        [Fact]
        public void EnsureSuccess_Success_Passes()
        {
            GitHubAccess.EnsureSuccess(Response(HttpStatusCode.OK));
        }

        [Fact]
        public void RepositoryNotFound_WithoutAToken_NeedsOneAsItMayBePrivate()
        {
            CredentialNeededException ex = Assert.IsType<CredentialNeededException>(GitHubAccess.RepositoryNotFound("owner", "repo", hasToken: false));

            Assert.Equal(CredentialProblem.Missing, ex.Need.Problem);
            Assert.Equal("owner/repo was not found on GitHub: it does not exist, or it is private and needs a githubToken.", ex.Message);
        }

        [Fact]
        public void RepositoryNotFound_WithAToken_SaysTheTokenMayNotReachIt()
        {
            Exception ex = GitHubAccess.RepositoryNotFound("owner", "repo", hasToken: true);

            Assert.IsType<InvalidOperationException>(ex);
            Assert.Equal("owner/repo was not found on GitHub: it does not exist, or the githubToken cannot read it.", ex.Message);
        }
    }
}
