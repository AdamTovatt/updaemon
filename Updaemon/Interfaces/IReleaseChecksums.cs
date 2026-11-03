namespace Updaemon.Interfaces
{
    /// <summary>
    /// Verifies a file downloaded from a GitHub release asset against the release's <c>SHA256SUMS</c>.
    /// </summary>
    public interface IReleaseChecksums
    {
        /// <summary>Verifies <paramref name="filePath"/>, downloaded from <paramref name="assetUrl"/>.</summary>
        /// <exception cref="Common.Utilities.ChecksumException">
        /// The release has no <c>SHA256SUMS</c>, it has no line for the asset, or the file does not match it.
        /// </exception>
        /// <exception cref="HttpRequestException">The <c>SHA256SUMS</c> could not be fetched.</exception>
        Task VerifyAsync(string assetUrl, string filePath, CancellationToken cancellationToken = default);
    }
}
