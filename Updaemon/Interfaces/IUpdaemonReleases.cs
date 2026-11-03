using Updaemon.Configuration;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Updaemon's own releases, which self-update installs from.
    /// </summary>
    public interface IUpdaemonReleases
    {
        /// <summary>The latest release's version; null when the release page names no x.y.z version.</summary>
        /// <exception cref="HttpRequestException">The release page could not be reached.</exception>
        Task<UpdaemonVersion?> GetLatestVersionAsync(CancellationToken cancellationToken = default);

        /// <summary>Downloads the release's updaemon build for this machine to <paramref name="destinationPath"/> and verifies it.</summary>
        /// <exception cref="HttpRequestException">The download failed, for example the release has no build for this machine.</exception>
        /// <exception cref="Common.Utilities.ChecksumException">The build does not match the release's SHA256SUMS, or cannot be checked against it.</exception>
        Task DownloadAsync(UpdaemonVersion release, string destinationPath, CancellationToken cancellationToken = default);
    }
}
