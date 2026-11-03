using Updaemon.Models;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Downloads distribution plugin binaries and retrieves their metadata.
    /// </summary>
    public interface IPluginDownloader
    {
        /// <summary>
        /// Downloads a plugin, saves it to a temporary file in the target directory, verifies it
        /// against <see cref="PluginDownload.Checksums"/> when there is one (a file that fails is
        /// deleted without being run), makes it executable, and retrieves its service information.
        /// The caller is responsible for moving or deleting TempFilePath in the result.
        /// </summary>
        /// <param name="download">What to download, and what to verify it against.</param>
        /// <param name="targetDirectory">The directory to save the temporary file in.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>Result containing the temp file path and service information.</returns>
        /// <exception cref="Common.Utilities.ChecksumException">The download does not match its SHA256SUMS, or the release has none.</exception>
        /// <exception cref="Common.Utilities.CredentialNeededException">From GitHub only: the token was rejected or the rate limit is used up.</exception>
        /// <exception cref="HttpRequestException">The download failed.</exception>
        Task<PluginDownloadResult> DownloadAndInspectAsync(PluginDownload download, string targetDirectory, CancellationToken cancellationToken = default);
    }
}
