using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Tests.Mocks
{
    /// <summary>A config whose next saves of an app can be made to fail, as on a disk that is full for a moment.</summary>
    public sealed class SaveFailingConfigManager : IConfigManager
    {
        private readonly IConfigManager _inner;

        public SaveFailingConfigManager(IConfigManager inner)
        {
            _inner = inner;
        }

        /// <summary>What the next saves of an app do, in order; the ones after that work. Null is one that works.</summary>
        public Queue<Exception?> NextSaves { get; } = new Queue<Exception?>();

        public Task SaveAppAsync(InstalledApp app, CancellationToken cancellationToken = default)
        {
            return NextSaves.TryDequeue(out Exception? failure) && failure != null ? Task.FromException(failure) : _inner.SaveAppAsync(app, cancellationToken);
        }

        public Task<UpdaemonConfig> LoadConfigAsync(CancellationToken cancellationToken = default) => _inner.LoadConfigAsync(cancellationToken);

        public Task SaveConfigAsync(UpdaemonConfig config, CancellationToken cancellationToken = default) => _inner.SaveConfigAsync(config, cancellationToken);

        public Task<InstalledApp?> GetAppAsync(string name, CancellationToken cancellationToken = default) => _inner.GetAppAsync(name, cancellationToken);

        public Task<IReadOnlyList<InstalledApp>> GetAllAppsAsync(CancellationToken cancellationToken = default) => _inner.GetAllAppsAsync(cancellationToken);

        public Task RemoveAppAsync(string name, CancellationToken cancellationToken = default) => _inner.RemoveAppAsync(name, cancellationToken);

        public Task AddOrUpdatePluginAsync(InstalledPluginInfo pluginInfo, CancellationToken cancellationToken = default) => _inner.AddOrUpdatePluginAsync(pluginInfo, cancellationToken);

        public Task<InstalledPluginInfo?> GetPluginAsync(string alias, CancellationToken cancellationToken = default) => _inner.GetPluginAsync(alias, cancellationToken);

        public Task<IReadOnlyDictionary<string, InstalledPluginInfo>> GetAllPluginsAsync(CancellationToken cancellationToken = default) => _inner.GetAllPluginsAsync(cancellationToken);

        public Task RemovePluginAsync(string alias, CancellationToken cancellationToken = default) => _inner.RemovePluginAsync(alias, cancellationToken);
    }
}
