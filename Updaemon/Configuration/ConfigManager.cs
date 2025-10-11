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

        public ConfigManager()
        {
            _configFilePath = Path.Combine(ConfigDirectory, ConfigFileName);
        }

        public async Task<UpdaemonConfig> LoadConfigAsync()
        {
            if (!File.Exists(_configFilePath))
            {
                return new UpdaemonConfig();
            }

            string json = await File.ReadAllTextAsync(_configFilePath);
            UpdaemonConfig? config = JsonSerializer.Deserialize(json, UpdaemonJsonContext.Default.UpdaemonConfig);
            return config ?? new UpdaemonConfig();
        }

        public async Task SaveConfigAsync(UpdaemonConfig config)
        {
            Directory.CreateDirectory(ConfigDirectory);
            string json = JsonSerializer.Serialize(config, UpdaemonJsonContext.Default.UpdaemonConfig);
            await File.WriteAllTextAsync(_configFilePath, json);
        }

        public async Task RegisterServiceAsync(string localName, string remoteName)
        {
            UpdaemonConfig config = await LoadConfigAsync();
            
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

            await SaveConfigAsync(config);
        }

        public async Task SetRemoteNameAsync(string localName, string remoteName)
        {
            UpdaemonConfig config = await LoadConfigAsync();
            
            RegisteredService? service = config.Services.FirstOrDefault(s => s.LocalName == localName);
            if (service == null)
            {
                throw new InvalidOperationException($"Service '{localName}' is not registered.");
            }

            service.RemoteName = remoteName;
            await SaveConfigAsync(config);
        }

        public async Task<RegisteredService?> GetServiceAsync(string localName)
        {
            UpdaemonConfig config = await LoadConfigAsync();
            return config.Services.FirstOrDefault(s => s.LocalName == localName);
        }

        public async Task<IReadOnlyList<RegisteredService>> GetAllServicesAsync()
        {
            UpdaemonConfig config = await LoadConfigAsync();
            return config.Services.AsReadOnly();
        }

        public async Task SetDistributionPluginPathAsync(string pluginPath)
        {
            UpdaemonConfig config = await LoadConfigAsync();
            config.DistributionPluginPath = pluginPath;
            await SaveConfigAsync(config);
        }

        public async Task<string?> GetDistributionPluginPathAsync()
        {
            UpdaemonConfig config = await LoadConfigAsync();
            return config.DistributionPluginPath;
        }
    }
}

