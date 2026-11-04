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
                        Name = GitHubAccess.TokenSecret,
                        IsRequired = false,
                        Description = "GitHub personal access token. Required for private repositories and to avoid rate limiting."
                    }
                }
            };
        }

        public Task InitializeAsync(SecretCollection secrets, CancellationToken cancellationToken = default)
        {
            _githubToken = secrets.GetValueIgnoreCase(GitHubAccess.TokenSecret);
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

        public async Task<DownloadResult> DownloadVersionAsync(
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
            ExpectedSha256? expected = await ReadExpectedAsync(owner, repo, release, asset, cancellationToken);

            // Ensure target directory exists
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            string targetFilePath = Path.Combine(targetPath, asset.Name);
            await _apiClient.DownloadReleaseAssetAsync(owner, repo, asset.Id, targetFilePath, _githubToken, cancellationToken);
            if (expected != null)
            {
                try
                {
                    await expected.VerifyAsync(targetFilePath, cancellationToken);
                }
                catch (ChecksumException ex)
                {
                    throw NotInstalled(owner, repo, release, ex);
                }
            }

            // Extracts a .zip or .tar.gz; only a verified (or unverifiable) download gets this far.
            await _postProcessor.ProcessAsync(targetPath, cancellationToken);
            return new DownloadResult { Verified = expected != null };
        }

        /// <summary>
        /// What the asset must match, from the release's <c>SHA256SUMS</c>, read before the download. Null when
        /// the release has none; throws when it has one without a line for the asset.
        /// </summary>
        private async Task<ExpectedSha256?> ReadExpectedAsync(string owner, string repo, GithubRelease release, GithubAsset asset, CancellationToken cancellationToken)
        {
            GithubAsset? sumsAsset = release.Assets.FirstOrDefault(candidate => candidate.Name == Sha256Sums.AssetName);
            if (sumsAsset == null)
            {
                return null;
            }

            string sums = await _apiClient.ReadReleaseAssetTextAsync(owner, repo, sumsAsset.Id, _githubToken, cancellationToken);
            try
            {
                return Sha256Sums.Parse(sums).For(asset.Name);
            }
            catch (ChecksumException ex)
            {
                throw NotInstalled(owner, repo, release, ex);
            }
        }

        private static InvalidOperationException NotInstalled(string owner, string repo, GithubRelease release, ChecksumException ex) =>
            new InvalidOperationException($"The release {release.TagName} of {owner}/{repo} was not installed: {ex.Message}");

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
        /// (wildcards like *.zip); without one, the build for this machine, named *-&lt;rid&gt;
        /// with one of <see cref="DownloadPostProcessor.ArchiveEndings"/> or none. The release's
        /// SHA256SUMS is never one. Anything but exactly one match fails with the release's asset
        /// names, one per line, and how to pick one.
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

            string[] buildEndings = BuildEndings(runtimeIdentifier);
            Func<GithubAsset, bool> matches = pattern == null
                ? asset => buildEndings.Any(ending => asset.Name.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
                : WildcardMatcher(pattern);
            GithubAsset[] matchingAssets = assets.Where(asset => asset.Name != Sha256Sums.AssetName && matches(asset)).ToArray();
            if (matchingAssets.Length == 1)
            {
                return matchingAssets[0];
            }

            string[] buildNames = buildEndings.Select(ending => $"*{ending}").ToArray();
            string buildName = $"{string.Join(", ", buildNames[..^1])} or {buildNames[^1]}";
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

        /// <summary>How the build for <paramref name="runtimeIdentifier"/> may end: each archive, then a bare binary.</summary>
        private static string[] BuildEndings(string runtimeIdentifier)
        {
            return [.. DownloadPostProcessor.ArchiveEndings.Select(ending => $"-{runtimeIdentifier}{ending}"), $"-{runtimeIdentifier}"];
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

