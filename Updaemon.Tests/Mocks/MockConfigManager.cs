using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IConfigManager with in-memory storage.
    /// </summary>
    public class MockConfigManager : IConfigManager
    {
        private UpdaemonConfig _config = new UpdaemonConfig();
        public List<string> MethodCalls { get; } = new List<string>();

        public Task<UpdaemonConfig> LoadConfigAsync(CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(nameof(LoadConfigAsync));
            return Task.FromResult(_config);
        }

        public Task SaveConfigAsync(UpdaemonConfig config, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(nameof(SaveConfigAsync));
            _config = config;
            return Task.CompletedTask;
        }

        public Task<InstalledApp?> GetAppAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_config.Apps.FirstOrDefault(app => app.Name == name));
        }

        public Task<IReadOnlyList<InstalledApp>> GetAllAppsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<InstalledApp>>(_config.Apps.ToList());
        }

        public Task SaveAppAsync(InstalledApp app, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(SaveAppAsync)}:{app.Name}");
            _config.Apps.RemoveAll(existing => existing.Name == app.Name);
            _config.Apps.Add(app);
            return Task.CompletedTask;
        }

        public Task RemoveAppAsync(string name, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(RemoveAppAsync)}:{name}");
            _config.Apps.RemoveAll(app => app.Name == name);
            return Task.CompletedTask;
        }

        public Task AddOrUpdatePluginAsync(InstalledPluginInfo pluginInfo, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(AddOrUpdatePluginAsync)}:{pluginInfo.Alias}:{pluginInfo.Path}");
            _config.InstalledPlugins[pluginInfo.Alias] = pluginInfo;
            return Task.CompletedTask;
        }

        public Task<InstalledPluginInfo?> GetPluginAsync(string alias, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_config.InstalledPlugins.GetValueOrDefault(alias));
        }

        public Task<IReadOnlyDictionary<string, InstalledPluginInfo>> GetAllPluginsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<string, InstalledPluginInfo>>(_config.InstalledPlugins);
        }

        public Task RemovePluginAsync(string alias, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(RemovePluginAsync)}:{alias}");
            _config.InstalledPlugins.Remove(alias);
            return Task.CompletedTask;
        }
    }
}
