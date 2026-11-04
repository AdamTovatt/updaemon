namespace Updaemon.Services
{
    /// <summary>
    /// Reads a GitHub repository's latest release tag from where <c>releases/latest</c> redirects
    /// (<c>.../releases/tag/&lt;tag&gt;</c>), as install.sh does: unlike the API, that has no rate limit.
    /// GitHub's latest release is the one marked Latest; drafts and pre-releases never are.
    /// </summary>
    internal static class GitHubLatestRelease
    {
        private const string TagPathMarker = "/releases/tag/";

        /// <summary>The latest release's tag; null when the repository has no release.</summary>
        /// <exception cref="HttpRequestException">GitHub could not be reached, or has no such repository (status 404).</exception>
        public static async Task<string?> ReadTagAsync(HttpClient httpClient, string owner, string repo, CancellationToken cancellationToken)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, $"https://github.com/{owner}/{repo}/releases/latest"))
            using (HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                return TagFromUrl(response.RequestMessage?.RequestUri);
            }
        }

        /// <summary>The tag in a release page's address, after the redirects; null for any other page.</summary>
        internal static string? TagFromUrl(Uri? url)
        {
            // A repository without releases redirects to its release list instead.
            string path = url?.AbsolutePath ?? string.Empty;
            int marker = path.IndexOf(TagPathMarker, StringComparison.Ordinal);
            string tag = marker < 0 ? string.Empty : Uri.UnescapeDataString(path[(marker + TagPathMarker.Length)..]);
            return tag.Length > 0 ? tag : null;
        }
    }
}
