using System.Text.RegularExpressions;
using Updaemon.Common;
using Updaemon.Common.Models;
using Updaemon.Common.Utilities;
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

        public DistributionServiceInformation GetServiceInformation()
        {
            return new DistributionServiceInformation
            {
                FullName = "GitHub Releases",
                DefaultAlias = "github",
                Description = "Distribution service that uses GitHub Releases.",
                Version = "0.2.0",
                Secrets = new List<DistributionSecretInfo>
                {
                    new DistributionSecretInfo
                    {
                        Name = "githubToken",
                        IsRequired = false,
                        Description = "GitHub personal access token. Required for private repositories and to avoid rate limiting."
                    }
                }
            };
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
            string runtimeIdentifier,
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

            GithubAsset asset = SelectAsset(release.Assets, filenamePattern, runtimeIdentifier, owner, repo, release.TagName);

            // Ensure target directory exists
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            // Download the asset
            string targetFilePath = Path.Combine(targetPath, asset.Name);
            await _apiClient.DownloadReleaseAssetAsync(owner, repo, asset.Id, targetFilePath, _githubToken, cancellationToken);

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
                throw new ArgumentException($"Invalid GitHub remote '{serviceName}': expected owner/repo or owner/repo/<pattern>.");
            }

            string owner = parts[0];
            string repo = parts[1];
            string? filenamePattern = parts.Length > 2 ? parts[2] : null;

            return (owner, repo, filenamePattern);
        }

        /// <summary>
        /// Picks the one asset to download. With a pattern, that is the asset matching it
        /// (wildcards like *.zip); without one, the build for this machine, named
        /// *-&lt;rid&gt;.zip or *-&lt;rid&gt;. Anything but exactly one match fails with the release's
        /// asset names, one per line, and how to pick one.
        /// </summary>
        private static GithubAsset SelectAsset(
            GithubAsset[] assets,
            string? pattern,
            string runtimeIdentifier,
            string owner,
            string repo,
            string tagName)
        {
            string releaseLabel = $"release {tagName} of {owner}/{repo}";
            if (assets.Length == 0)
            {
                throw new InvalidOperationException($"The {releaseLabel} has no assets.");
            }

            Func<GithubAsset, bool> matches = pattern == null
                ? asset => IsBuildFor(asset.Name, runtimeIdentifier)
                : WildcardMatcher(pattern);
            GithubAsset[] matchingAssets = assets.Where(matches).ToArray();
            if (matchingAssets.Length == 1)
            {
                return matchingAssets[0];
            }

            string buildName = $"*-{runtimeIdentifier}.zip or *-{runtimeIdentifier}";
            string problem = (pattern, matchingAssets.Length) switch
            {
                (null, 0) => $"No asset in the {releaseLabel} is named {buildName}, the build for this machine.",
                (null, _) => $"Several assets in the {releaseLabel} are named {buildName}, the build for this machine.",
                (_, 0) => $"No asset in the {releaseLabel} matches '{pattern}'.",
                _ => $"Several assets in the {releaseLabel} match '{pattern}'.",
            };

            throw new InvalidOperationException(
                $"{problem} Its assets:\n" +
                string.Join("\n", assets.Select(asset => $"  {asset.Name}")) + "\n" +
                $"Pick one with the remote {owner}/{repo}/<pattern>, where <pattern> is an asset name and * matches any text.");
        }

        private static bool IsBuildFor(string assetName, string runtimeIdentifier)
        {
            return assetName.EndsWith($"-{runtimeIdentifier}.zip", StringComparison.OrdinalIgnoreCase)
                || assetName.EndsWith($"-{runtimeIdentifier}", StringComparison.OrdinalIgnoreCase);
        }

        private static Func<GithubAsset, bool> WildcardMatcher(string pattern)
        {
            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            Regex regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
            return asset => regex.IsMatch(asset.Name);
        }
    }
}

