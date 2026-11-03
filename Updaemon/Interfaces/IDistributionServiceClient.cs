using Updaemon.Common.Models;
using Updaemon.Models;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Client for communicating with distribution service plugins via named pipes.
    /// A call the plugin fails throws <see cref="PluginException"/> with the plugin's message.
    /// </summary>
    public interface IDistributionServiceClient : IAsyncDisposable
    {
        /// <summary>
        /// Starts the plugin executable, connects to it and reads its service information.
        /// A plugin speaking another plugin protocol version is refused.
        /// </summary>
        /// <param name="pluginExecutablePath">Path to the plugin executable.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>The plugin's service information.</returns>
        Task<DistributionServiceInformation> ConnectAsync(string pluginExecutablePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Initializes the distribution service with secrets.
        /// </summary>
        /// <param name="secrets">Nullable string containing zero or more key=value pairs separated by line breaks. Null if no secrets configured.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        Task InitializeAsync(string? secrets, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the latest version available for a service.
        /// </summary>
        /// <param name="serviceName">The remote service name to check.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>The latest version, or null if no version is available.</returns>
        Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a specific version of a service to the target path. The plugin is told
        /// this machine's runtime identifier so it can pick the matching build.
        /// </summary>
        /// <param name="serviceName">The remote service name to download.</param>
        /// <param name="version">The version to download.</param>
        /// <param name="targetPath">The directory path where the version should be downloaded.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>Whether the plugin verified the download against a checksum its release published.</returns>
        Task<DownloadResult> DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default);
    }
}
