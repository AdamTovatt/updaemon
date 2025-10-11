namespace Updaemon.Common
{
    /// <summary>
    /// Interface for distribution service plugins that manage version checking and downloads.
    /// Plugins are separate AOT-compiled executables that communicate via named pipes.
    /// </summary>
    public interface IDistributionService
    {
        /// <summary>
        /// Initializes the distribution service with secrets.
        /// </summary>
        /// <param name="secrets">Collection of secret key-value pairs.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        Task InitializeAsync(SecretCollection secrets, CancellationToken cancellationToken = default);

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

