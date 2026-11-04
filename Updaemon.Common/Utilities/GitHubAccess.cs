using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Updaemon.Common.Models;

namespace Updaemon.Common.Utilities
{
    /// <summary>
    /// How a request to GitHub is made and how GitHub refuses one for want of a token: a rejected token
    /// (401), a used-up rate limit (403 or 429 with no requests remaining), or a repository it does not
    /// show to a request without a token, which may be private. Each refusal is a
    /// <see cref="CredentialNeededException"/> naming <see cref="TokenSecret"/>.
    /// </summary>
    public static class GitHubAccess
    {
        /// <summary>The secret that holds a GitHub token.</summary>
        public const string TokenSecret = "githubToken";

        /// <summary>Whether <paramref name="token"/> is one: a blank value is none.</summary>
        public static bool HasToken(string? token) => !string.IsNullOrWhiteSpace(token);

        /// <summary>
        /// A GET with the user agent the API requires and the token, if any. With <paramref name="asset"/>,
        /// it asks for a release asset's bytes, which the API otherwise answers with the asset's description.
        /// </summary>
        public static HttpRequestMessage Get(Uri address, string? token, bool asset = false)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.UserAgent.ParseAdd("updaemon");
            if (asset)
            {
                request.Headers.Accept.ParseAdd("application/octet-stream");
            }

            if (HasToken(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return request;
        }

        /// <summary>
        /// The response for release <paramref name="which"/> of a repository (<c>latest</c> or
        /// <c>tags/&lt;escaped tag&gt;</c>), its body not yet read; null when the repository has no such release.
        /// </summary>
        /// <exception cref="CredentialNeededException">The repository was not found without a token, the token was rejected, or the rate limit is used up.</exception>
        /// <exception cref="InvalidOperationException">The repository was not found with a token.</exception>
        /// <exception cref="HttpRequestException">Any other failure.</exception>
        public static async Task<HttpResponseMessage?> GetReleaseAsync(HttpClient httpClient, string owner, string repo, string which, string? token, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await SendAsync(httpClient, new Uri($"https://api.github.com/repos/{owner}/{repo}/releases/{which}"), token, cancellationToken);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                return EnsureSuccessOrDispose(response);
            }

            // GitHub answers 404 both for a missing release and for a repository it does not show
            // (missing, or private to this request). Only the repository says which.
            response.Dispose();
            using (HttpResponseMessage repository = await SendAsync(httpClient, new Uri($"https://api.github.com/repos/{owner}/{repo}"), token, cancellationToken))
            {
                if (repository.StatusCode == HttpStatusCode.NotFound)
                {
                    throw RepositoryNotFound(owner, repo, HasToken(token));
                }

                EnsureSuccess(repository);
                return null;
            }
        }

        /// <summary>A release asset's bytes, by its API address (as a private repository needs) or its download address.</summary>
        /// <exception cref="CredentialNeededException">The token was rejected or the rate limit is used up.</exception>
        /// <exception cref="HttpRequestException">Any other failure, a 404 included.</exception>
        public static async Task<HttpResponseMessage> GetAssetAsync(HttpClient httpClient, Uri address, string? token, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            using (HttpRequestMessage request = Get(address, token, asset: true))
            {
                // GitHub's redirect to the file drops the token on the way.
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }

            return EnsureSuccessOrDispose(response);
        }

        /// <summary>Throws when <paramref name="response"/> did not succeed: as a credential need where it is one.</summary>
        /// <exception cref="CredentialNeededException">GitHub rejected the token or its rate limit is used up.</exception>
        /// <exception cref="HttpRequestException">Any other failure.</exception>
        public static void EnsureSuccess(HttpResponseMessage response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw TokenRejected();
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                && Header(response, "x-ratelimit-remaining") == "0")
            {
                string until = long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
                    ? $" until {DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)} UTC"
                    : string.Empty;
                throw new CredentialNeededException(
                    new CredentialNeed { Secret = TokenSecret, Problem = CredentialProblem.RateLimited },
                    $"GitHub's rate limit is used up{until}.");
            }

            response.EnsureSuccessStatusCode();
        }

        /// <summary>The refusal of a token that is set: GitHub answered 401.</summary>
        public static CredentialNeededException TokenRejected()
        {
            return new CredentialNeededException(
                new CredentialNeed { Secret = TokenSecret, Problem = CredentialProblem.Rejected },
                $"GitHub rejected the {TokenSecret}: it is wrong, expired or revoked.");
        }

        /// <summary>
        /// The failure for a repository GitHub says it does not have. Without a token it may be private,
        /// so the token is what is needed; with one, the token may not reach it.
        /// </summary>
        public static Exception RepositoryNotFound(string owner, string repo, bool hasToken)
        {
            return hasToken
                ? new InvalidOperationException($"{owner}/{repo} was not found on GitHub: it does not exist, or the {TokenSecret} cannot read it.")
                : new CredentialNeededException(
                    new CredentialNeed { Secret = TokenSecret, Problem = CredentialProblem.Missing },
                    $"{owner}/{repo} was not found on GitHub: it does not exist, or it is private and needs a {TokenSecret}.");
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpClient httpClient, Uri address, string? token, CancellationToken cancellationToken)
        {
            using (HttpRequestMessage request = Get(address, token))
            {
                return await httpClient.SendAsync(request, cancellationToken);
            }
        }

        private static HttpResponseMessage EnsureSuccessOrDispose(HttpResponseMessage response)
        {
            try
            {
                EnsureSuccess(response);
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        private static string? Header(HttpResponseMessage response, string name)
        {
            return response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
        }
    }
}
