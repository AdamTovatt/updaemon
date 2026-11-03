using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Updaemon's GitHub releases. The latest version is read from where <c>releases/latest</c>
    /// redirects to (<c>.../releases/tag/v1.2.0</c>), as install.sh does: unlike the API, that has no
    /// rate limit. Each release carries <c>&lt;asset&gt;-&lt;rid&gt;</c> builds: updaemon's, and the
    /// first-party plugins'.
    /// </summary>
    public class GitHubUpdaemonReleases : IUpdaemonReleases
    {
        private const string ReleasesUrl = "https://github.com/AdamTovatt/updaemon/releases";
        private const string UpdaemonAssetName = "Updaemon";
        private const string TagPrefix = "/releases/tag/v";

        private readonly HttpClient _httpClient;
        private readonly HostPlatform _platform;

        public GitHubUpdaemonReleases(HttpClient httpClient, HostPlatform platform)
        {
            _httpClient = httpClient;
            _platform = platform;
        }

        /// <summary>The build of asset <paramref name="assetBaseName"/> for <paramref name="runtimeIdentifier"/> in release <paramref name="release"/>.</summary>
        public static string AssetUrl(UpdaemonVersion release, string assetBaseName, string runtimeIdentifier)
        {
            return $"{ReleasesUrl}/download/v{release}/{assetBaseName}-{runtimeIdentifier}";
        }

        public async Task<UpdaemonVersion?> GetLatestVersionAsync(CancellationToken cancellationToken = default)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, $"{ReleasesUrl}/latest"))
            using (HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                return ParseTagUrl(response.RequestMessage?.RequestUri);
            }
        }

        public async Task DownloadAsync(UpdaemonVersion release, string destinationPath, CancellationToken cancellationToken = default)
        {
            string url = AssetUrl(release, UpdaemonAssetName, _platform.RuntimeIdentifier);
            using (HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using (FileStream file = File.Create(destinationPath))
                {
                    await response.Content.CopyToAsync(file, cancellationToken);
                }
            }
        }

        /// <summary>
        /// The version in a release page's address, after the redirects: <c>.../releases/tag/v1.2.0</c>.
        /// A tag that is not <c>v</c> and three numbers names no release updaemon can install.
        /// </summary>
        internal static UpdaemonVersion? ParseTagUrl(Uri? url)
        {
            string? path = url?.AbsolutePath;
            int tag = path?.IndexOf(TagPrefix, StringComparison.Ordinal) ?? -1;
            return tag >= 0 && UpdaemonVersion.TryParse(path![(tag + TagPrefix.Length)..], out UpdaemonVersion? version)
                ? version
                : null;
        }
    }
}
