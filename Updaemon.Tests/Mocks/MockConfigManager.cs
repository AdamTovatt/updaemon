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

        public async Task RegisterServiceAsync(string localName, string remoteName, string distributionPluginAlias, ServiceType serviceType = ServiceType.Service, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(RegisterServiceAsync)}:{localName}:{remoteName}:{distributionPluginAlias}:{serviceType}");
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);

            RegisteredService? existing = config.Services.FirstOrDefault(s => s.LocalName == localName);
            if (existing != null)
            {
                throw new InvalidOperationException($"Service '{localName}' is already registered.");
            }

            config.Services.Add(new RegisteredService
            {
                LocalName = localName,
                RemoteName = remoteName,
                DistributionPluginAlias = distributionPluginAlias,
                ServiceType = serviceType,
            });

            await SaveConfigAsync(config, cancellationToken);
        }

        public async Task SetRemoteNameAsync(string localName, string remoteName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(SetRemoteNameAsync)}:{localName}:{remoteName}");
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);

            RegisteredService? service = config.Services.FirstOrDefault(s => s.LocalName == localName);
            if (service == null)
            {
                throw new InvalidOperationException($"Service '{localName}' is not registered.");
            }

            service.RemoteName = remoteName;
            await SaveConfigAsync(config, cancellationToken);
        }

        public async Task SetExecutableNameAsync(string localName, string? executableName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(SetExecutableNameAsync)}:{localName}:{executableName ?? "null"}");
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);

            RegisteredService? service = config.Services.FirstOrDefault(s => s.LocalName == localName);
            if (service == null)
            {
                throw new InvalidOperationException($"Service '{localName}' is not registered.");
            }

            service.ExecutableName = executableName;
            await SaveConfigAsync(config, cancellationToken);
        }

        public async Task<RegisteredService?> GetServiceAsync(string localName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(GetServiceAsync)}:{localName}");
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.Services.FirstOrDefault(s => s.LocalName == localName);
        }

        public async Task<IReadOnlyList<RegisteredService>> GetAllServicesAsync(CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(nameof(GetAllServicesAsync));
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.Services.AsReadOnly();
        }

        public async Task AddOrUpdatePluginAsync(InstalledPluginInfo pluginInfo, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(AddOrUpdatePluginAsync)}:{pluginInfo.Alias}");
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            config.InstalledPlugins[pluginInfo.Alias] = pluginInfo;
            await SaveConfigAsync(config, cancellationToken);
        }

        public async Task<InstalledPluginInfo?> GetPluginAsync(string alias, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(GetPluginAsync)}:{alias}");
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.InstalledPlugins.TryGetValue(alias, out InstalledPluginInfo? plugin) ? plugin : null;
        }

        public async Task<IReadOnlyDictionary<string, InstalledPluginInfo>> GetAllPluginsAsync(CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(nameof(GetAllPluginsAsync));
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.InstalledPlugins;
        }

        public async Task RemovePluginAsync(string alias, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(RemovePluginAsync)}:{alias}");
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            config.InstalledPlugins.Remove(alias);
            await SaveConfigAsync(config, cancellationToken);
        }
    }
}

