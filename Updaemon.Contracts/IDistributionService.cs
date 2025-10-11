namespace Updaemon.Contracts
{
    /// <summary>
    /// Interface for distribution service plugins that manage version checking and downloads.
    /// Plugins are separate AOT-compiled executables that communicate via named pipes.
    /// </summary>
    public interface IDistributionService
    {
        /// <summary>
        /// Initializes the distribution service with optional secrets.
        /// </summary>
        /// <param name="secrets">Nullable string containing zero or more key=value pairs separated by line breaks. Null if no secrets configured.</param>
        Task InitializeAsync(string? secrets);

        /// <summary>
        /// Gets the latest version available for a service.
        /// </summary>
        /// <param name="serviceName">The remote service name to check.</param>
        /// <returns>The latest version, or null if no version is available.</returns>
        Task<Version?> GetLatestVersionAsync(string serviceName);

        /// <summary>
        /// Downloads a specific version of a service to the target path.
        /// </summary>
        /// <param name="serviceName">The remote service name to download.</param>
        /// <param name="version">The version to download.</param>
        /// <param name="targetPath">The directory path where the version should be downloaded.</param>
        Task DownloadVersionAsync(string serviceName, Version version, string targetPath);
    }
}

