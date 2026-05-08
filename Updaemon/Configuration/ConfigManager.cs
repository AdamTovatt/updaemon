using System.Text.Json;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Serialization;

namespace Updaemon.Configuration
{
    /// <summary>
    /// Manages the updaemon configuration stored in /var/lib/updaemon/config.json
    /// </summary>
    public class ConfigManager : IConfigManager
    {
        private const string ConfigFileName = "config.json";

        private readonly string _configFilePath;
        private readonly string _configDirectory;

        public ConfigManager()
            : this(PlatformPaths.ConfigDirectory)
        {
        }

        public ConfigManager(string configDirectory)
        {
            _configDirectory = configDirectory;
            _configFilePath = Path.Combine(_configDirectory, ConfigFileName);
        }

        public async Task<UpdaemonConfig> LoadConfigAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_configFilePath))
            {
                return new UpdaemonConfig();
            }

            string json = await File.ReadAllTextAsync(_configFilePath, cancellationToken);
            UpdaemonConfig? config = JsonSerializer.Deserialize(json, UpdaemonJsonContext.Default.UpdaemonConfig);
            return config ?? new UpdaemonConfig();
        }

        public async Task SaveConfigAsync(UpdaemonConfig config, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(_configDirectory);
            string json = JsonSerializer.Serialize(config, UpdaemonJsonContext.Default.UpdaemonConfig);
            await File.WriteAllTextAsync(_configFilePath, json, cancellationToken);
        }

        public async Task RegisterServiceAsync(string localName, string remoteName, string distributionPluginAlias, ServiceType serviceType = ServiceType.Service, CancellationToken cancellationToken = default)
        {
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
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.Services.FirstOrDefault(s => s.LocalName == localName);
        }

        public async Task<IReadOnlyList<RegisteredService>> GetAllServicesAsync(CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.Services.AsReadOnly();
        }

        public async Task AddOrUpdatePluginAsync(InstalledPluginInfo pluginInfo, CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            config.InstalledPlugins[pluginInfo.Alias] = pluginInfo;
            await SaveConfigAsync(config, cancellationToken);
        }

        public async Task<InstalledPluginInfo?> GetPluginAsync(string alias, CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.InstalledPlugins.TryGetValue(alias, out InstalledPluginInfo? plugin) ? plugin : null;
        }

        public async Task<IReadOnlyDictionary<string, InstalledPluginInfo>> GetAllPluginsAsync(CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.InstalledPlugins;
        }

        public async Task RemovePluginAsync(string alias, CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            config.InstalledPlugins.Remove(alias);
            await SaveConfigAsync(config, cancellationToken);
        }
    }
}

