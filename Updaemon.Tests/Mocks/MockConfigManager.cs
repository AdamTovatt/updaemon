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

        public Task<UpdaemonConfig> LoadConfigAsync()
        {
            MethodCalls.Add(nameof(LoadConfigAsync));
            return Task.FromResult(_config);
        }

        public Task SaveConfigAsync(UpdaemonConfig config)
        {
            MethodCalls.Add(nameof(SaveConfigAsync));
            _config = config;
            return Task.CompletedTask;
        }

        public async Task RegisterServiceAsync(string localName, string remoteName)
        {
            MethodCalls.Add($"{nameof(RegisterServiceAsync)}:{localName}:{remoteName}");
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
            MethodCalls.Add($"{nameof(SetRemoteNameAsync)}:{localName}:{remoteName}");
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
            MethodCalls.Add($"{nameof(GetServiceAsync)}:{localName}");
            UpdaemonConfig config = await LoadConfigAsync();
            return config.Services.FirstOrDefault(s => s.LocalName == localName);
        }

        public async Task<IReadOnlyList<RegisteredService>> GetAllServicesAsync()
        {
            MethodCalls.Add(nameof(GetAllServicesAsync));
            UpdaemonConfig config = await LoadConfigAsync();
            return config.Services.AsReadOnly();
        }

        public async Task SetDistributionPluginPathAsync(string pluginPath)
        {
            MethodCalls.Add($"{nameof(SetDistributionPluginPathAsync)}:{pluginPath}");
            UpdaemonConfig config = await LoadConfigAsync();
            config.DistributionPluginPath = pluginPath;
            await SaveConfigAsync(config);
        }

        public async Task<string?> GetDistributionPluginPathAsync()
        {
            MethodCalls.Add(nameof(GetDistributionPluginPathAsync));
            UpdaemonConfig config = await LoadConfigAsync();
            return config.DistributionPluginPath;
        }
    }
}

