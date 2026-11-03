using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Updaemon's GitHub releases, tagged <c>v1.2.0</c>; the latest is read as
    /// <see cref="GitHubLatestRelease"/> does. Each release carries <c>&lt;asset&gt;-&lt;rid&gt;</c>
    /// builds: updaemon's, and the first-party plugins'.
    /// </summary>
    public class GitHubUpdaemonReleases : IUpdaemonReleases
    {
        private const string Owner = "AdamTovatt";
        private const string Repo = "updaemon";
        private const string ReleasesUrl = $"https://github.com/{Owner}/{Repo}/releases";
        private const string UpdaemonAssetName = "Updaemon";

        private readonly HttpClient _httpClient;
        private readonly IReleaseChecksums _checksums;
        private readonly HostPlatform _platform;

        public GitHubUpdaemonReleases(HttpClient httpClient, IReleaseChecksums checksums, HostPlatform platform)
        {
            _httpClient = httpClient;
            _checksums = checksums;
            _platform = platform;
        }

        /// <summary>The build of asset <paramref name="assetBaseName"/> for <paramref name="runtimeIdentifier"/> in release <paramref name="release"/>.</summary>
        public static string AssetUrl(UpdaemonVersion release, string assetBaseName, string runtimeIdentifier)
        {
            return $"{ReleasesUrl}/download/v{release}/{assetBaseName}-{runtimeIdentifier}";
        }

        public async Task<UpdaemonVersion?> GetLatestVersionAsync(CancellationToken cancellationToken = default)
        {
            return VersionOfTag(await GitHubLatestRelease.ReadTagAsync(_httpClient, Owner, Repo, cancellationToken));
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

            Uri asset = new Uri(url);
            await _checksums.VerifyAsync(ReleaseChecksums.BesideAsset(asset), Path.GetFileName(asset.LocalPath), destinationPath, gitHub: null, cancellationToken);
        }

        /// <summary>The version a release tag names: <c>v1.2.0</c>. Any other tag names no release updaemon can install.</summary>
        private static UpdaemonVersion? VersionOfTag(string? tag)
        {
            return tag != null && tag.StartsWith('v') && UpdaemonVersion.TryParse(tag[1..], out UpdaemonVersion? version) ? version : null;
        }
    }
}
