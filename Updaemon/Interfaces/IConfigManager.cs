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
        Task<UpdaemonConfig> LoadConfigAsync();

        /// <summary>
        /// Saves the configuration to disk.
        /// </summary>
        Task SaveConfigAsync(UpdaemonConfig config);

        /// <summary>
        /// Registers a new service.
        /// </summary>
        Task RegisterServiceAsync(string localName, string remoteName);

        /// <summary>
        /// Updates the remote name for an existing service.
        /// </summary>
        Task SetRemoteNameAsync(string localName, string remoteName);

        /// <summary>
        /// Gets a registered service by local name.
        /// </summary>
        Task<RegisteredService?> GetServiceAsync(string localName);

        /// <summary>
        /// Gets all registered services.
        /// </summary>
        Task<IReadOnlyList<RegisteredService>> GetAllServicesAsync();

        /// <summary>
        /// Sets the active distribution service plugin path.
        /// </summary>
        Task SetDistributionPluginPathAsync(string pluginPath);

        /// <summary>
        /// Gets the active distribution service plugin path.
        /// </summary>
        Task<string?> GetDistributionPluginPathAsync();
    }
}

