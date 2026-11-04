using Updaemon.GithubDistributionService.Models;

namespace Updaemon.GithubDistributionService.Interfaces
{
    /// <summary>
    /// Client for interacting with the GitHub API. Every call throws
    /// <see cref="Common.Utilities.CredentialNeededException"/> when GitHub rejects the token or its
    /// rate limit is used up.
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
        /// <returns>The latest release, or null when the repository has no release.</returns>
        /// <exception cref="Common.Utilities.CredentialNeededException">The repository was not found without a token: it may be private.</exception>
        /// <exception cref="InvalidOperationException">The repository was not found with a token: the token may not reach it.</exception>
        Task<GithubRelease?> GetLatestReleaseAsync(string owner, string repo, string? token, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a release asset via the GitHub API to a target file path.
        /// Works for private repositories when authenticated.
        /// </summary>
        /// <param name="owner">The repository owner</param>
        /// <param name="repo">The repository name</param>
        /// <param name="assetId">The release asset id</param>
        /// <param name="targetFilePath">The path where the file should be saved</param>
        /// <param name="token">Optional GitHub token for authentication</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task DownloadReleaseAssetAsync(
            string owner,
            string repo,
            long assetId,
            string targetFilePath,
            string? token,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads a small text release asset, such as SHA256SUMS, via the GitHub API.
        /// Works for private repositories when authenticated.
        /// </summary>
        Task<string> ReadReleaseAssetTextAsync(string owner, string repo, long assetId, string? token, CancellationToken cancellationToken = default);

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
