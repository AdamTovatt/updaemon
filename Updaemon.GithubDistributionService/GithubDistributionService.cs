using Updaemon.Common;
using Updaemon.GithubDistributionService.Interfaces;
using Updaemon.GithubDistributionService.Models;

namespace Updaemon.GithubDistributionService
{
    /// <summary>
    /// Distribution service that retrieves versioned releases from GitHub.
    /// </summary>
    public class GithubDistributionService : IDistributionService
    {
        private readonly IGithubApiClient _apiClient;
        private readonly IVersionParser _versionParser;
        private readonly IDownloadPostProcessor _postProcessor;
        private string? _githubToken;

        public GithubDistributionService(
            IGithubApiClient apiClient,
            IVersionParser versionParser,
            IDownloadPostProcessor postProcessor)
        {
            _apiClient = apiClient;
            _versionParser = versionParser;
            _postProcessor = postProcessor;
        }

        public Task InitializeAsync(SecretCollection secrets, CancellationToken cancellationToken = default)
        {
            _githubToken = secrets.GetValueIgnoreCase("githubToken");
            return Task.CompletedTask;
        }

        public async Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            (string owner, string repo, string? _) = ParseServiceName(serviceName);

            GithubRelease? release = await _apiClient.GetLatestReleaseAsync(owner, repo, _githubToken, cancellationToken);
            if (release == null)
            {
                return null;
            }

            // Parse version from tag name - we don't care about assets here
            Version? version = _versionParser.Parse(release.TagName);
            return version;
        }

        public async Task DownloadVersionAsync(
            string serviceName,
            Version version,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            (string owner, string repo, string? filenamePattern) = ParseServiceName(serviceName);

            // Get the latest release (we should get the same one that was checked in GetLatestVersionAsync)
            GithubRelease? release = await _apiClient.GetLatestReleaseAsync(owner, repo, _githubToken, cancellationToken);
            if (release == null)
            {
                throw new InvalidOperationException($"Release not found for {owner}/{repo}");
            }

            // Find the asset to download based on pattern
            GithubAsset? asset = FindMatchingAsset(release.Assets, filenamePattern, owner, repo, release.TagName);

            // Ensure target directory exists
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            // Download the asset
            string targetFilePath = Path.Combine(targetPath, asset.Name);
            await _apiClient.DownloadAssetAsync(asset.BrowserDownloadUrl, targetFilePath, _githubToken, cancellationToken);

            // Post-process (extract if zip)
            await _postProcessor.ProcessAsync(targetPath, cancellationToken);
        }

        /// <summary>
        /// Parses a service name into owner, repo, and optional filename pattern.
        /// Handles leading/trailing slashes gracefully.
        /// </summary>
        /// <param name="serviceName">Service name in format "owner/repo" or "owner/repo/pattern"</param>
        /// <returns>Tuple of (owner, repo, filenamePattern)</returns>
        private (string owner, string repo, string? filenamePattern) ParseServiceName(string serviceName)
        {
            // Strip leading and trailing slashes
            string normalized = serviceName.Trim('/');

            string[] parts = normalized.Split('/');

            if (parts.Length < 2)
            {
                throw new ArgumentException($"Invalid service name format: '{serviceName}'. Expected 'owner/repo' or 'owner/repo/pattern'");
            }

            string owner = parts[0];
            string repo = parts[1];
            string? filenamePattern = parts.Length > 2 ? parts[2] : null;

            return (owner, repo, filenamePattern);
        }

        /// <summary>
        /// Finds a matching asset from a list based on a filename pattern.
        /// Supports wildcards like *.zip, linux-*.zip, etc.
        /// </summary>
        /// <param name="assets">Array of available assets</param>
        /// <param name="pattern">Filename pattern (null means single asset required)</param>
        /// <param name="owner">Repository owner (for error messages)</param>
        /// <param name="repo">Repository name (for error messages)</param>
        /// <param name="tagName">Release tag name (for error messages)</param>
        /// <returns>The matching asset</returns>
        private GithubAsset FindMatchingAsset(GithubAsset[] assets, string? pattern, string owner, string repo, string tagName)
        {
            if (pattern == null)
            {
                // No pattern specified - require exactly one asset
                if (assets.Length == 0)
                {
                    throw new InvalidOperationException($"No assets found in release {tagName} for {owner}/{repo}");
                }

                if (assets.Length > 1)
                {
                    string assetNames = string.Join(", ", assets.Select(a => a.Name));
                    throw new InvalidOperationException(
                        $"Multiple assets found in release {tagName} for {owner}/{repo}: {assetNames}. " +
                        $"Please specify a pattern like '{owner}/{repo}/*.zip'");
                }

                return assets[0];
            }

            // Convert wildcard pattern to regex
            string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(
                regexPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            GithubAsset[] matchingAssets = assets.Where(a => regex.IsMatch(a.Name)).ToArray();

            if (matchingAssets.Length == 0)
            {
                string assetNames = string.Join(", ", assets.Select(a => a.Name));
                throw new InvalidOperationException(
                    $"No assets matching pattern '{pattern}' found in release {tagName} for {owner}/{repo}. " +
                    $"Available assets: {assetNames}");
            }

            if (matchingAssets.Length > 1)
            {
                string matchingNames = string.Join(", ", matchingAssets.Select(a => a.Name));
                throw new InvalidOperationException(
                    $"Multiple assets match pattern '{pattern}' in release {tagName} for {owner}/{repo}: {matchingNames}. " +
                    $"Please use a more specific pattern.");
            }

            return matchingAssets[0];
        }
    }
}

