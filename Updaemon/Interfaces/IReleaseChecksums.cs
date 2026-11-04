using Updaemon.Common.Utilities;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Reads, from a GitHub release's <c>SHA256SUMS</c>, the SHA-256 one of its assets must match. It is read
    /// before the asset is downloaded, so a release that cannot be verified fails without a download.
    /// </summary>
    public interface IReleaseChecksums
    {
        /// <summary>The SHA-256 that asset <paramref name="assetName"/> must match.</summary>
        /// <param name="checksumsUrl">The release's <c>SHA256SUMS</c>.</param>
        /// <param name="gitHub">Set to fetch it from GitHub, with its token if any; null to fetch the address as it is.</param>
        /// <exception cref="ChecksumException">The release has no <c>SHA256SUMS</c>, or it has no line for the asset.</exception>
        /// <exception cref="CredentialNeededException">From GitHub only: the token was rejected or the rate limit is used up.</exception>
        /// <exception cref="HttpRequestException">The <c>SHA256SUMS</c> could not be fetched.</exception>
        Task<ExpectedSha256> ReadExpectedAsync(Uri checksumsUrl, string assetName, Models.GitHubFetch? gitHub, CancellationToken cancellationToken = default);
    }
}
