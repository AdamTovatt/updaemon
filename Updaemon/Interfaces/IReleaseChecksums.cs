namespace Updaemon.Interfaces
{
    /// <summary>
    /// Verifies a file downloaded from a GitHub release asset against the release's <c>SHA256SUMS</c>.
    /// </summary>
    public interface IReleaseChecksums
    {
        /// <summary>Verifies <paramref name="filePath"/>, downloaded as asset <paramref name="assetName"/>.</summary>
        /// <param name="checksumsUrl">The release's <c>SHA256SUMS</c>.</param>
        /// <param name="gitHub">Set to fetch it from GitHub, with its token if any; null to fetch the address as it is.</param>
        /// <exception cref="Common.Utilities.ChecksumException">
        /// The release has no <c>SHA256SUMS</c>, it has no line for the asset, or the file does not match it.
        /// </exception>
        /// <exception cref="Common.Utilities.CredentialNeededException">From GitHub only: the token was rejected or the rate limit is used up.</exception>
        /// <exception cref="HttpRequestException">The <c>SHA256SUMS</c> could not be fetched.</exception>
        Task VerifyAsync(Uri checksumsUrl, string assetName, string filePath, Models.GitHubFetch? gitHub, CancellationToken cancellationToken = default);
    }
}
