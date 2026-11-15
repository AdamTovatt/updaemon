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
                Version = typeof(GithubDistributionService).Assembly.GetName().Version!.ToString(3),
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

            ReleaseAssets<GithubAsset> assets = new ReleaseAssets<GithubAsset>(
                $"release {release.TagName} of {owner}/{repo}",
                $"{owner}/{repo}",
                release.Assets,
                asset => asset.Name,
                (asset, token) => _apiClient.ReadReleaseAssetTextAsync(owner, repo, asset.Id, _githubToken, token),
                (asset, filePath, token) => _apiClient.DownloadReleaseAssetAsync(owner, repo, asset.Id, filePath, _githubToken, token),
                _postProcessor);
            return await assets.DownloadAsync(filenamePattern, runtimeIdentifier, targetPath, cancellationToken);
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
    }
}

