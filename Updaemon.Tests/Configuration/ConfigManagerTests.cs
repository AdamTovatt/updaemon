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
                ConfigManager configManager = new ConfigManager(TestScope.Create(tempHelper.TempDirectory));

                UpdaemonConfig config = await configManager.LoadConfigAsync();

                Assert.Empty(config.Apps);
                Assert.Empty(config.InstalledPlugins);
            }
        }

        [Theory]
        [InlineData("{\"installedPlugins\":{\"github\":{\"alias\":\"github\",\"version\":\"1.0.0\"}},\"services\":[{\"localName\":\"api\"}]}", "is from updaemon 0.x")]
        [InlineData("{\"format\":2,\"apps\":[]}", "has format 2")]
        public async Task LoadConfigAsync_OtherFormat_FailsWithOneClearLine(string json, string expected)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory);
                Directory.CreateDirectory(scope.RootDirectory);
                await File.WriteAllTextAsync(scope.ConfigFilePath, json);
                ConfigManager configManager = new ConfigManager(scope);

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => configManager.LoadConfigAsync());

                Assert.Contains(expected, exception.Message);
            }
        }

        [Fact]
        public async Task SaveAppAsync_RoundTripsEveryField()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(TestScope.Create(tempHelper.TempDirectory));
                InstalledApp app = new InstalledApp
                {
                    Name = "byteshelf",
                    Plugin = "github",
                    Remote = "AdamTovatt/byteshelf",
                    Manifest = new AppManifest { Executable = "byteshelf", Command = "byteshelf", Service = new ServiceSpec { Args = { "sync", "run" } } },
                    Settings = { ["BYTESHELF_URL"] = "https://shelf.example.com" },
                    Args = new List<string> { "serve" },
                    RunAs = "byteshelf",
                    AutoRestart = false,
                };

                await configManager.SaveAppAsync(app);
                InstalledApp? loaded = await new ConfigManager(TestScope.Create(tempHelper.TempDirectory)).GetAppAsync("byteshelf");

                Assert.NotNull(loaded);
                Assert.Equal("github:AdamTovatt/byteshelf", loaded.Source);
                Assert.Equal("byteshelf", loaded.Manifest!.Command);
                Assert.Equal(new[] { "sync", "run" }, loaded.Manifest.Service!.Args);
                Assert.Equal("https://shelf.example.com", loaded.Settings["BYTESHELF_URL"]);
                Assert.Equal(new[] { "serve" }, loaded.Args!);
                Assert.Equal("byteshelf", loaded.RunAs);
                Assert.False(loaded.AutoRestart);
            }
        }

        [Fact]
        public async Task SaveAppAsync_AppWithSameName_IsReplaced()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(TestScope.Create(tempHelper.TempDirectory));
                await configManager.SaveAppAsync(new InstalledApp { Name = "a", Remote = "old" });
                await configManager.SaveAppAsync(new InstalledApp { Name = "b", Remote = "b" });

                await configManager.SaveAppAsync(new InstalledApp { Name = "a", Remote = "new" });

                IReadOnlyList<InstalledApp> apps = await configManager.GetAllAppsAsync();
                Assert.Equal(new[] { ("b", "b"), ("a", "new") }.Order(), apps.Select(app => (app.Name, app.Remote)).Order());
            }
        }

        [Fact]
        public async Task RemoveAppAsync_RemovesOnlyThatApp()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(TestScope.Create(tempHelper.TempDirectory));
                await configManager.SaveAppAsync(new InstalledApp { Name = "a" });
                await configManager.SaveAppAsync(new InstalledApp { Name = "b" });

                await configManager.RemoveAppAsync("a");

                Assert.Equal(new[] { "b" }, (await configManager.GetAllAppsAsync()).Select(app => app.Name));
            }
        }

        [Fact]
        public async Task Plugins_AddUpdateAndRemove()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                ConfigManager configManager = new ConfigManager(TestScope.Create(tempHelper.TempDirectory));

                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("github", "/old"));
                Assert.Equal(new PluginSource.Registry("github"), (await configManager.GetPluginAsync("github"))!.Source);
                PluginSource urlSource = new PluginSource.Url("https://example.com/plugin");
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = "/new", Source = urlSource });
                Assert.Equal("/new", (await configManager.GetPluginAsync("github"))!.Path);
                Assert.Equal(urlSource, (await configManager.GetPluginAsync("github"))!.Source);
                Assert.Single(await configManager.GetAllPluginsAsync());

                await configManager.RemovePluginAsync("github");
                await configManager.RemovePluginAsync("missing");
                Assert.Null(await configManager.GetPluginAsync("github"));
            }
        }
    }
}
