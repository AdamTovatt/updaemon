using Updaemon.Commands;
using Updaemon.Common.Models;
using Updaemon.Configuration;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class DistUpdateCommandTests
    {
        private static DistributionServiceInformation Information(string version)
        {
            return new DistributionServiceInformation { FullName = "Plugin", DefaultAlias = "plugin", Version = version };
        }

        private static DistUpdateCommand Command(
            TempFileHelper tempHelper,
            MockConfigManager configManager,
            MockOutputWriter outputWriter,
            MockPluginDownloader pluginDownloader,
            Version? updaemonVersion = null)
        {
            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(
                new Dictionary<string, string>
                {
                    ["github"] = "Updaemon.GithubDistributionService",
                    ["byteshelf"] = "Updaemon.Distribution.ByteShelfDistribution",
                },
                new HostPlatform(false, "linux-arm64"));
            PluginInstaller installer = new PluginInstaller(resolver, pluginDownloader, configManager, TestScope.Create(tempHelper.TempDirectory));
            return new DistUpdateCommand(configManager, outputWriter, installer, pluginDownloader, new UpdaemonVersion(updaemonVersion ?? new Version(1, 0, 0)));
        }

        [Fact]
        public async Task ExecuteAsync_NewerVersionAvailable_UpdatesPlugin()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader { ServiceInformation = Information("0.4.0") };
                string pluginPath = tempHelper.CreateTempFile("plugins/github/Updaemon.GithubDistributionService", "old");
                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("github", pluginPath));
                pluginDownloader.LocalServiceInformationByPath[pluginPath] = Information("0.3.0");

                int exitCode = await Command(tempHelper, configManager, outputWriter, pluginDownloader).ExecuteAsync(new[] { "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(outputWriter.Messages, m => m.Contains("Updated 'github' to version 0.4.0"));
                InstalledPluginInfo? recorded = await configManager.GetPluginAsync("github");
                Assert.NotNull(recorded);
                Assert.True(File.Exists(recorded.Path));
            }
        }

        [Fact]
        public async Task ExecuteAsync_AlreadyUpToDate_SaysSo()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader { ServiceInformation = Information("1.0.0") };
                string pluginPath = tempHelper.CreateTempFile("plugins/github/Updaemon.GithubDistributionService-linux-arm64", "current");
                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("github", pluginPath));
                pluginDownloader.LocalServiceInformationByPath[pluginPath] = Information("1.0.0");

                int exitCode = await Command(tempHelper, configManager, outputWriter, pluginDownloader).ExecuteAsync(new[] { "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(outputWriter.Messages, m => m.Contains("Plugin 'github' is already up to date."));
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginNotInstalled_ReturnsError()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockOutputWriter outputWriter = new MockOutputWriter();

                int exitCode = await Command(tempHelper, new MockConfigManager(), outputWriter, new MockPluginDownloader()).ExecuteAsync(new[] { "non-existent" });

                Assert.Equal(1, exitCode);
                Assert.Contains(outputWriter.Errors, e => e.Contains("not installed"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_NoPluginsInstalled_ReturnsSuccess()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockOutputWriter outputWriter = new MockOutputWriter();

                int exitCode = await Command(tempHelper, new MockConfigManager(), outputWriter, new MockPluginDownloader()).ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.Contains(outputWriter.Messages, m => m.Contains("No distribution plugins installed"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginInstalledFromUrl_IsSkipped()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader();
                string pluginPath = tempHelper.CreateTempFile("plugins/custom/custom-plugin", "custom");
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo
                {
                    Alias = "custom",
                    Path = pluginPath,
                    Source = new PluginSource.Url("https://example.com/custom-plugin"),
                });

                int exitCode = await Command(tempHelper, configManager, outputWriter, pluginDownloader).ExecuteAsync(new[] { "custom" });

                Assert.Equal(0, exitCode);
                Assert.Contains(outputWriter.Messages, m => m.Contains("Skipping 'custom': it was installed from a URL."));
                Assert.Empty(pluginDownloader.MethodCalls);
            }
        }

        [Fact]
        public async Task ExecuteAsync_RegistryPluginUnderAnotherAlias_UpdatesFromTheRunningReleaseByRegistryName()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader { ServiceInformation = Information("0.5.0") };
                string pluginPath = tempHelper.CreateTempFile("plugins/gh/Updaemon.GithubDistributionService-linux-arm64", "old");
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo
                {
                    Alias = "gh",
                    Path = pluginPath,
                    Source = new PluginSource.Registry("github"),
                });

                int exitCode = await Command(tempHelper, configManager, outputWriter, pluginDownloader, new Version(1, 4, 0)).ExecuteAsync(new[] { "gh" });

                Assert.Equal(0, exitCode);
                Assert.Contains(
                    "DownloadAndInspectAsync:https://github.com/AdamTovatt/updaemon/releases/download/v1.4.0/Updaemon.GithubDistributionService-linux-arm64",
                    pluginDownloader.MethodCalls);
                InstalledPluginInfo? recorded = await configManager.GetPluginAsync("gh");
                Assert.NotNull(recorded);
                Assert.Equal(new PluginSource.Registry("github"), recorded.Source);
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginBinaryMissing_StillInstallsIt()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader { ServiceInformation = Information("1.0.0") };
                string pluginPath = Path.Combine(tempHelper.CreateTempDirectory("plugins/github"), "missing-binary");
                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("github", pluginPath));

                int exitCode = await Command(tempHelper, configManager, outputWriter, pluginDownloader).ExecuteAsync(new[] { "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(outputWriter.Messages, m => m.Contains("Updated 'github' to version 1.0.0"));
                InstalledPluginInfo? recorded = await configManager.GetPluginAsync("github");
                Assert.NotNull(recorded);
                Assert.True(File.Exists(recorded.Path));
            }
        }

        [Fact]
        public async Task ExecuteAsync_UpdateAllPlugins_ChecksEachOne()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader { ServiceInformation = Information("1.0.0") };
                string githubPath = tempHelper.CreateTempFile("plugins/github/Updaemon.GithubDistributionService-linux-arm64", "github");
                string byteshelfPath = tempHelper.CreateTempFile("plugins/byteshelf/Updaemon.Distribution.ByteShelfDistribution-linux-arm64", "byteshelf");
                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("github", githubPath));
                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("byteshelf", byteshelfPath));
                pluginDownloader.LocalServiceInformationByPath[githubPath] = Information("1.0.0");
                pluginDownloader.LocalServiceInformationByPath[byteshelfPath] = Information("1.0.0");

                int exitCode = await Command(tempHelper, configManager, outputWriter, pluginDownloader).ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.Contains(outputWriter.Messages, m => m.Contains("Plugin 'github' is already up to date."));
                Assert.Contains(outputWriter.Messages, m => m.Contains("Plugin 'byteshelf' is already up to date."));
                Assert.Contains(outputWriter.Messages, m => m.Contains("All plugins are up to date"));
            }
        }
    }
}
