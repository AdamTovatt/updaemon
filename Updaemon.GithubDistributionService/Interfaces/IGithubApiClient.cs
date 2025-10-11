using Updaemon.GithubDistributionService.Models;

namespace Updaemon.GithubDistributionService.Interfaces
{
    /// <summary>
    /// Client for interacting with the GitHub API.
    /// </summary>
    public interface IGithubApiClient
    {
        /// <summary>
        /// Gets the latest release for a GitHub repository.
        /// </summary>
        /// <param name="owner">The repository owner</param>
        /// <param name="repo">The repository name</param>
        /// <param name="token">Optional GitHub token for authentication</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The latest release, or null if not found</returns>
        Task<GithubRelease?> GetLatestReleaseAsync(string owner, string repo, string? token, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads an asset from a URL to a target file path.
        /// </summary>
        /// <param name="url">The download URL</param>
        /// <param name="targetFilePath">The path where the file should be saved</param>
        /// <param name="token">Optional GitHub token for authentication</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task DownloadAssetAsync(string url, string targetFilePath, string? token, CancellationToken cancellationToken = default);
    }
}

