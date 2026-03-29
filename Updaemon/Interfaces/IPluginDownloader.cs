using Updaemon.Common.Models;
using Updaemon.Models;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Downloads distribution plugin binaries and retrieves their metadata.
    /// </summary>
    public interface IPluginDownloader
    {
        /// <summary>
        /// Downloads a plugin from a URL, saves it to a temporary file in the target directory,
        /// makes it executable, and retrieves its service information.
        /// The caller is responsible for moving or deleting TempFilePath in the result.
        /// </summary>
        /// <param name="downloadUrl">The URL to download the plugin binary from.</param>
        /// <param name="targetDirectory">The directory to save the temporary file in.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>Result containing the temp file path and service information.</returns>
        Task<PluginDownloadResult> DownloadAndInspectAsync(string downloadUrl, string targetDirectory, CancellationToken cancellationToken = default);

        /// <summary>
        /// Connects to an already-installed plugin binary and retrieves its service information.
        /// </summary>
        /// <param name="pluginPath">Path to the installed plugin executable.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>The plugin's service information.</returns>
        Task<DistributionServiceInformation> InspectLocalAsync(string pluginPath, CancellationToken cancellationToken = default);
    }
}
