namespace Updaemon.Interfaces
{
    /// <summary>
    /// Client for communicating with distribution service plugins via named pipes.
    /// </summary>
    public interface IDistributionServiceClient : IAsyncDisposable
    {
        /// <summary>
        /// Connects to the distribution service plugin executable.
        /// </summary>
        /// <param name="pluginExecutablePath">Path to the plugin executable.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        Task ConnectAsync(string pluginExecutablePath, CancellationToken cancellationToken = default);

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
        /// Downloads a specific version of a service to the target path.
        /// </summary>
        /// <param name="serviceName">The remote service name to download.</param>
        /// <param name="version">The version to download.</param>
        /// <param name="targetPath">The directory path where the version should be downloaded.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        Task DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default);
    }
}

