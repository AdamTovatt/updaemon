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
        [InlineData("{\"installedPlugins\":{\"github\":{\"alias\":\"github\",\"version\":\"1.0.0\"}},\"services\":[{\"localName\":\"api\"}]}", "is from updaemon 0.x and not converted yet. Convert it with 'sudo updaemon migrate'.")]
        [InlineData("{\"format\":null}", "cannot be read: it is not an object with a whole-number 'format'.")]
        [InlineData("[]", "cannot be read: it is not an object with a whole-number 'format'.")]        [InlineData("{\"format\":2,\"apps\":[]}", "has format 2")]
        [InlineData("{\"format\":1,\"installedPlugins\":{\"github\":{\"alias\":\"github\",\"path\":\"/p\",\"source\":{\"kind\":\"registry\",\"name\":\"github\"}}}}", "config.json cannot be read: ")]
        [InlineData("{\"format\":1,\"installedPlugins\":{\"github\":{\"alias\":\"github\",\"path\":\"/p\",\"source\":{\"kind\":\"registry\",\"name\":\"github\",\"release\":\"1.3\"}}}}", "cannot be read: '1.3' is not an updaemon release version (x.y.z).")]
        public async Task LoadConfigAsync_OtherFormatOrUnreadable_FailsWithOneClearLine(string json, string expected)
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
                Assert.Equal(new PluginSource.Registry("github", AppHarness.RunningVersion), (await configManager.GetPluginAsync("github"))!.Source);
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

        [Theory]
        [InlineData("""{"kind":"github","owner":"someone","repo":"updaemon-s3","tag":"v2.1.0"}""", null)]
        [InlineData("""{"kind":"github","owner":"someone","repo":"updaemon-s3","tag":"v2.1.0","skippedTag":"v2.2.0"}""", "v2.2.0")]
        public async Task Plugins_GitHubSourceAsStored_IsRead(string sourceJson, string? skippedTag)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory);
                Directory.CreateDirectory(scope.RootDirectory);
                await File.WriteAllTextAsync(scope.ConfigFilePath, """{"format":1,"installedPlugins":{"s3":{"alias":"s3","path":"/p","source":""" + sourceJson + "}}}");

                PluginSource source = (await new ConfigManager(scope).GetPluginAsync("s3"))!.Source;

                Assert.Equal(new PluginSource.GitHub("someone", "updaemon-s3", "v2.1.0") { SkippedTag = skippedTag }, source);
            }
        }

        [Fact]
        public async Task Plugins_GitHubSource_IsStoredWithItsTag()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory);
                ConfigManager configManager = new ConfigManager(scope);

                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "s3", Path = "/p", Source = new PluginSource.GitHub("someone", "updaemon-s3", "v2.1.0") });

                string json = await File.ReadAllTextAsync(scope.ConfigFilePath);
                Assert.Contains("\"kind\": \"github\"", json);
                Assert.Contains("\"owner\": \"someone\"", json);
                Assert.Contains("\"repo\": \"updaemon-s3\"", json);
                Assert.Contains("\"tag\": \"v2.1.0\"", json);
                Assert.DoesNotContain("skippedTag", json);
            }
        }
    }
}
