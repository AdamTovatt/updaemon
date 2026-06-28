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
                Assert.Empty(config.InstalledPlugins);
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
                    InstalledPlugins = new Dictionary<string, InstalledPluginInfo>
                    {
                        { "github", new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" } }
                    },
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
                Assert.Single(loadedConfig.InstalledPlugins);
                Assert.Equal("/path/to/plugin", loadedConfig.InstalledPlugins["github"].Path);
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

                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

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

                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await configManager.RegisterServiceAsync("my-api", "MyApi2", "github")
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

                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");
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

                await configManager.RegisterServiceAsync("service1", "Service1", "github");
                await configManager.RegisterServiceAsync("service2", "Service2", "github");
                await configManager.RegisterServiceAsync("service3", "Service3", "github");

                IReadOnlyList<RegisteredService> services = await configManager.GetAllServicesAsync();

                Assert.Equal(3, services.Count);
                Assert.Contains(services, s => s.LocalName == "service1");
                Assert.Contains(services, s => s.LocalName == "service2");
                Assert.Contains(services, s => s.LocalName == "service3");
            }
        }

        [Fact]
        public async Task AddOrUpdatePluginAsync_And_GetPluginAsync_Success()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                InstalledPluginInfo pluginInfo = new InstalledPluginInfo
                {
                    Alias = "github",
                    Path = "/path/to/my/plugin"
                };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                InstalledPluginInfo? retrieved = await configManager.GetPluginAsync("github");
                Assert.NotNull(retrieved);
                Assert.Equal("/path/to/my/plugin", retrieved.Path);
            }
        }

        [Fact]
        public async Task GetPluginAsync_NoPluginConfigured_ReturnsNull()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                InstalledPluginInfo? plugin = await configManager.GetPluginAsync("github");

                Assert.Null(plugin);
            }
        }

        [Fact]
        public async Task GetAllPluginsAsync_ReturnsAllInstalledPlugins()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = "/path/to/github" });
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "byteshelf", Path = "/path/to/byteshelf" });

                IReadOnlyDictionary<string, InstalledPluginInfo> plugins = await configManager.GetAllPluginsAsync();

                Assert.Equal(2, plugins.Count);
                Assert.True(plugins.ContainsKey("github"));
                Assert.True(plugins.ContainsKey("byteshelf"));
                Assert.Equal("/path/to/github", plugins["github"].Path);
                Assert.Equal("/path/to/byteshelf", plugins["byteshelf"].Path);
            }
        }

        [Fact]
        public async Task AddOrUpdatePluginAsync_UpdatesExistingPlugin()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = "/old/path" });
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = "/new/path" });

                InstalledPluginInfo? plugin = await configManager.GetPluginAsync("github");
                Assert.NotNull(plugin);
                Assert.Equal("/new/path", plugin.Path);
            }
        }

        [Fact]
        public async Task RemovePluginAsync_RemovesPlugin()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = "/path/to/github" });
                await configManager.RemovePluginAsync("github");

                InstalledPluginInfo? plugin = await configManager.GetPluginAsync("github");
                Assert.Null(plugin);
            }
        }

        [Fact]
        public async Task RemovePluginAsync_NonExistentPlugin_DoesNotThrow()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                // Should not throw when removing non-existent plugin
                await configManager.RemovePluginAsync("non-existent");
            }
        }

        [Fact]
        public async Task SetExecutableNameAsync_UpdatesExecutableName()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");
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

                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");
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
  ""installedPlugins"": {},
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

        [Fact]
        public async Task LoadConfigAsync_OldConfigWithoutServiceType_DefaultsToService()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string configPath = Path.Combine(tempHelper.TempDirectory, "config.json");
                string oldConfigJson = @"{
  ""installedPlugins"": {},
  ""services"": [
    {
      ""localName"": ""test-service"",
      ""remoteName"": ""TestService"",
      ""distributionPluginAlias"": ""github""
    }
  ]
}";
                Directory.CreateDirectory(tempHelper.TempDirectory);
                await File.WriteAllTextAsync(configPath, oldConfigJson);

                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);
                UpdaemonConfig config = await configManager.LoadConfigAsync();

                Assert.NotNull(config);
                Assert.Single(config.Services);
                Assert.Equal(ServiceType.Service, config.Services[0].ServiceType);
            }
        }

        [Fact]
        public async Task SetAutoRestartAsync_UpdatesValue()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                // Defaults to true for a freshly registered service.
                RegisteredService? service = await configManager.GetServiceAsync("my-api");
                Assert.NotNull(service);
                Assert.True(service.AutoRestart);

                await configManager.SetAutoRestartAsync("my-api", false);
                service = await configManager.GetServiceAsync("my-api");
                Assert.NotNull(service);
                Assert.False(service.AutoRestart);

                await configManager.SetAutoRestartAsync("my-api", true);
                service = await configManager.GetServiceAsync("my-api");
                Assert.NotNull(service);
                Assert.True(service.AutoRestart);
            }
        }

        [Fact]
        public async Task SetAutoRestartAsync_ServiceDoesNotExist_ThrowsException()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await configManager.SetAutoRestartAsync("non-existent", false)
                );

                Assert.Contains("not registered", exception.Message);
            }
        }

        [Fact]
        public async Task LoadConfigAsync_OldConfigWithoutAutoRestart_DefaultsToTrue()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string configPath = Path.Combine(tempHelper.TempDirectory, "config.json");
                string oldConfigJson = @"{
  ""installedPlugins"": {},
  ""services"": [
    {
      ""localName"": ""test-service"",
      ""remoteName"": ""TestService"",
      ""distributionPluginAlias"": ""github""
    }
  ]
}";
                Directory.CreateDirectory(tempHelper.TempDirectory);
                await File.WriteAllTextAsync(configPath, oldConfigJson);

                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);
                UpdaemonConfig config = await configManager.LoadConfigAsync();

                Assert.NotNull(config);
                Assert.Single(config.Services);
                Assert.True(config.Services[0].AutoRestart);
            }
        }

        [Fact]
        public async Task RegisterServiceAsync_WithCliType_PersistsType()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(tempHelper.TempDirectory);

                await configManager.RegisterServiceAsync("my-tool", "owner/tool", "github", ServiceType.Cli);

                UpdaemonConfig config = await configManager.LoadConfigAsync();
                Assert.Single(config.Services);
                Assert.Equal(ServiceType.Cli, config.Services[0].ServiceType);
            }
        }
    }
}

