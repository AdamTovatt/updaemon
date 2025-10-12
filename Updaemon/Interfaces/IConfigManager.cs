using Updaemon.Models;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Manages the updaemon configuration stored in /var/lib/updaemon/config.json
    /// </summary>
    public interface IConfigManager
    {
        /// <summary>
        /// Loads the configuration from disk.
        /// </summary>
        Task<UpdaemonConfig> LoadConfigAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves the configuration to disk.
        /// </summary>
        Task SaveConfigAsync(UpdaemonConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// Registers a new service.
        /// </summary>
        Task RegisterServiceAsync(string localName, string remoteName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the remote name for an existing service.
        /// </summary>
        Task SetRemoteNameAsync(string localName, string remoteName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the executable name for an existing service.
        /// Pass null to clear the executable name and use the local name instead.
        /// </summary>
        Task SetExecutableNameAsync(string localName, string? executableName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a registered service by local name.
        /// </summary>
        Task<RegisteredService?> GetServiceAsync(string localName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all registered services.
        /// </summary>
        Task<IReadOnlyList<RegisteredService>> GetAllServicesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the active distribution service plugin path.
        /// </summary>
        Task SetDistributionPluginPathAsync(string pluginPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the active distribution service plugin path.
        /// </summary>
        Task<string?> GetDistributionPluginPathAsync(CancellationToken cancellationToken = default);
    }
}

