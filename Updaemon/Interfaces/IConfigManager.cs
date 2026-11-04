using Updaemon.Models;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Reads and writes the scope's config.json.
    /// </summary>
    public interface IConfigManager
    {
        Task<UpdaemonConfig> LoadConfigAsync(CancellationToken cancellationToken = default);

        Task SaveConfigAsync(UpdaemonConfig config, CancellationToken cancellationToken = default);

        Task<InstalledApp?> GetAppAsync(string name, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<InstalledApp>> GetAllAppsAsync(CancellationToken cancellationToken = default);

        /// <summary>Adds the app, or replaces the stored app with the same name.</summary>
        Task SaveAppAsync(InstalledApp app, CancellationToken cancellationToken = default);

        Task RemoveAppAsync(string name, CancellationToken cancellationToken = default);

        Task AddOrUpdatePluginAsync(InstalledPluginInfo pluginInfo, CancellationToken cancellationToken = default);

        Task<InstalledPluginInfo?> GetPluginAsync(string alias, CancellationToken cancellationToken = default);

        Task<IReadOnlyDictionary<string, InstalledPluginInfo>> GetAllPluginsAsync(CancellationToken cancellationToken = default);

        Task RemovePluginAsync(string alias, CancellationToken cancellationToken = default);
    }
}
