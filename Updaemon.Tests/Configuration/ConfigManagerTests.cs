using Updaemon.Configuration;
using Updaemon.Models;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Configuration
{
    public class ConfigManagerTests
    {
        [Fact]
        public async Task LoadConfigAsync_NonExistentFile_ReturnsEmptyConfig()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                UpdaemonConfig config = await configManager.LoadConfigAsync();

                Assert.NotNull(config);
                Assert.Empty(config.Services);
                Assert.Null(config.DistributionPluginPath);
            }
        }

        [Fact]
        public async Task SaveConfigAsync_And_LoadConfigAsync_Roundtrip_Success()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                UpdaemonConfig originalConfig = new UpdaemonConfig
                {
                    DistributionPluginPath = "/path/to/plugin",
                    Services = new List<RegisteredService>
                    {
                        new RegisteredService
                        {
                            LocalName = "test-service",
                            RemoteName = "TestService",
                        },
                    },
                };

                await configManager.SaveConfigAsync(originalConfig);
                UpdaemonConfig loadedConfig = await configManager.LoadConfigAsync();

                Assert.NotNull(loadedConfig);
                Assert.Equal(originalConfig.DistributionPluginPath, loadedConfig.DistributionPluginPath);
                Assert.Single(loadedConfig.Services);
                Assert.Equal("test-service", loadedConfig.Services[0].LocalName);
                Assert.Equal("TestService", loadedConfig.Services[0].RemoteName);
            }
        }

        [Fact]
        public async Task RegisterServiceAsync_AddsService_AndSaves()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.RegisterServiceAsync("my-api", "MyApi");

                UpdaemonConfig config = await configManager.LoadConfigAsync();
                Assert.Single(config.Services);
                Assert.Equal("my-api", config.Services[0].LocalName);
                Assert.Equal("MyApi", config.Services[0].RemoteName);
            }
        }

        [Fact]
        public async Task RegisterServiceAsync_ServiceAlreadyExists_ThrowsException()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.RegisterServiceAsync("my-api", "MyApi");

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await configManager.RegisterServiceAsync("my-api", "MyApi2")
                );

                Assert.Contains("already registered", exception.Message);
            }
        }

        [Fact]
        public async Task SetRemoteNameAsync_UpdatesExistingService()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.RegisterServiceAsync("my-api", "MyApi");
                await configManager.SetRemoteNameAsync("my-api", "UpdatedApi");

                RegisteredService? service = await configManager.GetServiceAsync("my-api");
                Assert.NotNull(service);
                Assert.Equal("UpdatedApi", service.RemoteName);
            }
        }

        [Fact]
        public async Task SetRemoteNameAsync_ServiceDoesNotExist_ThrowsException()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await configManager.SetRemoteNameAsync("non-existent", "SomeName")
                );

                Assert.Contains("not registered", exception.Message);
            }
        }

        [Fact]
        public async Task GetServiceAsync_NonExistentService_ReturnsNull()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                RegisteredService? service = await configManager.GetServiceAsync("non-existent");

                Assert.Null(service);
            }
        }

        [Fact]
        public async Task GetAllServicesAsync_ReturnsAllRegisteredServices()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.RegisterServiceAsync("service1", "Service1");
                await configManager.RegisterServiceAsync("service2", "Service2");
                await configManager.RegisterServiceAsync("service3", "Service3");

                IReadOnlyList<RegisteredService> services = await configManager.GetAllServicesAsync();

                Assert.Equal(3, services.Count);
                Assert.Contains(services, s => s.LocalName == "service1");
                Assert.Contains(services, s => s.LocalName == "service2");
                Assert.Contains(services, s => s.LocalName == "service3");
            }
        }

        [Fact]
        public async Task SetDistributionPluginPathAsync_And_GetDistributionPluginPathAsync_Success()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.SetDistributionPluginPathAsync("/path/to/my/plugin");

                string? pluginPath = await configManager.GetDistributionPluginPathAsync();
                Assert.Equal("/path/to/my/plugin", pluginPath);
            }
        }

        [Fact]
        public async Task GetDistributionPluginPathAsync_NoPluginConfigured_ReturnsNull()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                string? pluginPath = await configManager.GetDistributionPluginPathAsync();

                Assert.Null(pluginPath);
            }
        }

        [Fact]
        public async Task SetExecutableNameAsync_UpdatesExecutableName()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.RegisterServiceAsync("my-api", "MyApi");
                await configManager.SetExecutableNameAsync("my-api", "MyApiExecutable");

                RegisteredService? service = await configManager.GetServiceAsync("my-api");
                Assert.NotNull(service);
                Assert.Equal("MyApiExecutable", service.ExecutableName);
            }
        }

        [Fact]
        public async Task SetExecutableNameAsync_WithNull_ClearsExecutableName()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.RegisterServiceAsync("my-api", "MyApi");
                await configManager.SetExecutableNameAsync("my-api", "MyApiExecutable");
                await configManager.SetExecutableNameAsync("my-api", null);

                RegisteredService? service = await configManager.GetServiceAsync("my-api");
                Assert.NotNull(service);
                Assert.Null(service.ExecutableName);
            }
        }

        [Fact]
        public async Task SetExecutableNameAsync_ServiceDoesNotExist_ThrowsException()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await configManager.SetExecutableNameAsync("non-existent", "SomeExecutable")
                );

                Assert.Contains("not registered", exception.Message);
            }
        }

        [Fact]
        public async Task LoadConfigAsync_OldConfigWithoutExecutableName_LoadsSuccessfully()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                // Write old-style config JSON without ExecutableName field
                string configPath = Path.Combine(tempHelper.TempDirectory, "config.json");
                string oldConfigJson = @"{
  ""distributionPluginPath"": ""/path/to/plugin"",
  ""services"": [
    {
      ""localName"": ""test-service"",
      ""remoteName"": ""TestService""
    }
  ]
}";
                Directory.CreateDirectory(tempHelper.TempDirectory);
                await File.WriteAllTextAsync(configPath, oldConfigJson);

                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);
                UpdaemonConfig config = await configManager.LoadConfigAsync();

                Assert.NotNull(config);
                Assert.Single(config.Services);
                Assert.Equal("test-service", config.Services[0].LocalName);
                Assert.Equal("TestService", config.Services[0].RemoteName);
                Assert.Null(config.Services[0].ExecutableName);
            }
        }
    }
}

