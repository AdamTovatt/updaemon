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
        private const string ConfigDirectory = "/var/lib/updaemon";
        private const string ConfigFileName = "config.json";

        private readonly string _configFilePath;
        private readonly string _configDirectory;

        public ConfigManager()
        {
            _configDirectory = ConfigDirectory;
            _configFilePath = Path.Combine(_configDirectory, ConfigFileName);
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

        public async Task RegisterServiceAsync(string localName, string remoteName, CancellationToken cancellationToken = default)
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

        public async Task SetDistributionPluginPathAsync(string pluginPath, CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            config.DistributionPluginPath = pluginPath;
            await SaveConfigAsync(config, cancellationToken);
        }

        public async Task<string?> GetDistributionPluginPathAsync(CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await LoadConfigAsync(cancellationToken);
            return config.DistributionPluginPath;
        }
    }
}

