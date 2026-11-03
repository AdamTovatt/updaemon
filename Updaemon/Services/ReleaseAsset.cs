using Updaemon.Common.Utilities;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>Fetches a release asset: through <see cref="GitHubAccess"/> for a github: source, as it is otherwise.</summary>
    internal static class ReleaseAsset
    {
        /// <summary>The response, its body not yet read.</summary>
        /// <exception cref="CredentialNeededException">From GitHub only: the token was rejected or the rate limit is used up.</exception>
        /// <exception cref="HttpRequestException">Any other failure, a 404 included.</exception>
        public static async Task<HttpResponseMessage> GetAsync(HttpClient httpClient, Uri address, GitHubFetch? gitHub, CancellationToken cancellationToken)
        {
            if (gitHub != null)
            {
                return await GitHubAccess.GetAssetAsync(httpClient, address, gitHub.Token, cancellationToken);
            }

            HttpResponseMessage response = await httpClient.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            try
            {
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
    }
}
