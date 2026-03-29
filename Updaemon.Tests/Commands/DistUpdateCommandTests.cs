using Updaemon.Commands;
using Updaemon.Common.Models;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class DistUpdateCommandTests
    {
        [Fact]
        public async Task ExecuteAsync_NewerVersionAvailable_UpdatesPlugin()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockPluginUrlResolver pluginUrlResolver = new MockPluginUrlResolver();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader();

                // Set up installed plugin
                string pluginPath = tempHelper.CreateTempFile("plugins/github", "Updaemon.GithubDistributionService");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                // Current version (returned when inspecting installed binary)
                pluginDownloader.LocalServiceInformationByPath[pluginPath] = new DistributionServiceInformation
                {
                    FullName = "GitHub Distribution", DefaultAlias = "github", Version = "0.3.0",
                    Secrets = new List<DistributionSecretInfo>(),
                };

                // New version (returned by download)
                pluginDownloader.ServiceInformation = new DistributionServiceInformation
                {
                    FullName = "GitHub Distribution", DefaultAlias = "github", Version = "0.4.0",
                    Secrets = new List<DistributionSecretInfo>(),
                };

                pluginUrlResolver.SetPluginUrl("github", "https://example.com/plugins/github-dist");

                DistUpdateCommand command = new DistUpdateCommand(
                    configManager, outputWriter, pluginUrlResolver, pluginDownloader);

                int exitCode = await command.ExecuteAsync(new[] { "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(outputWriter.Messages, m => m.Contains("Updated 'github' to version 0.4.0"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_AlreadyUpToDate_SkipsUpdate()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockPluginUrlResolver pluginUrlResolver = new MockPluginUrlResolver();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader();

                string pluginPath = tempHelper.CreateTempFile("plugins/github", "Updaemon.GithubDistributionService");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                // Both return same version
                DistributionServiceInformation sameVersionInfo = new DistributionServiceInformation
                {
                    FullName = "GitHub Distribution", DefaultAlias = "github", Version = "1.0.0",
                    Secrets = new List<DistributionSecretInfo>(),
                };
                pluginDownloader.LocalServiceInformationByPath[pluginPath] = sameVersionInfo;
                pluginDownloader.ServiceInformation = sameVersionInfo;

                pluginUrlResolver.SetPluginUrl("github", "https://example.com/plugins/github-dist");

                DistUpdateCommand command = new DistUpdateCommand(
                    configManager, outputWriter, pluginUrlResolver, pluginDownloader);

                int exitCode = await command.ExecuteAsync(new[] { "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(outputWriter.Messages, m => m.Contains("already up to date"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginNotInstalled_ReturnsError()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();

            DistUpdateCommand command = new DistUpdateCommand(
                configManager, outputWriter, new MockPluginUrlResolver(), new MockPluginDownloader());

            int exitCode = await command.ExecuteAsync(new[] { "non-existent" });

            Assert.Equal(1, exitCode);
            Assert.Contains(outputWriter.Errors, e => e.Contains("not installed"));
        }

        [Fact]
        public async Task ExecuteAsync_NoPluginsInstalled_ReturnsSuccess()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();

            DistUpdateCommand command = new DistUpdateCommand(
                configManager, outputWriter, new MockPluginUrlResolver(), new MockPluginDownloader());

            int exitCode = await command.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(0, exitCode);
            Assert.Contains(outputWriter.Messages, m => m.Contains("No distribution plugins installed"));
        }

        [Fact]
        public async Task ExecuteAsync_PluginNotInRegistry_SkipsWithMessage()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockPluginUrlResolver pluginUrlResolver = new MockPluginUrlResolver();
                // Don't register any URL — resolver will throw

                string pluginPath = tempHelper.CreateTempFile("plugins/custom", "custom-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "custom", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                DistUpdateCommand command = new DistUpdateCommand(
                    configManager, outputWriter, pluginUrlResolver, new MockPluginDownloader());

                int exitCode = await command.ExecuteAsync(new[] { "custom" });

                Assert.Equal(0, exitCode);
                Assert.Contains(outputWriter.Messages, m => m.Contains("not found in plugin registry"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginBinaryMissing_StillDownloadsUpdate()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockPluginUrlResolver pluginUrlResolver = new MockPluginUrlResolver();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader();

                // Register plugin with path that doesn't exist
                string pluginDir = tempHelper.CreateTempDirectory("plugins/github");
                string pluginPath = Path.Combine(pluginDir, "missing-binary");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                pluginDownloader.ServiceInformation = new DistributionServiceInformation
                {
                    FullName = "GitHub Distribution", DefaultAlias = "github", Version = "1.0.0",
                    Secrets = new List<DistributionSecretInfo>(),
                };

                pluginUrlResolver.SetPluginUrl("github", "https://example.com/plugins/github-dist");

                DistUpdateCommand command = new DistUpdateCommand(
                    configManager, outputWriter, pluginUrlResolver, pluginDownloader);

                int exitCode = await command.ExecuteAsync(new[] { "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(outputWriter.Messages, m => m.Contains("Updated 'github' to version 1.0.0"));
                // Binary should now exist at the plugin path
                Assert.True(File.Exists(pluginPath));
            }
        }

        [Fact]
        public async Task ExecuteAsync_UpdateAllPlugins_ChecksEachOne()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockPluginUrlResolver pluginUrlResolver = new MockPluginUrlResolver();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader();

                // Install two plugins
                string githubPath = tempHelper.CreateTempFile("plugins/github", "github-plugin");
                string byteshelfPath = tempHelper.CreateTempFile("plugins/byteshelf", "byteshelf-plugin");
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = githubPath });
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "byteshelf", Path = byteshelfPath });

                // Both already up to date
                DistributionServiceInformation sameVersionInfo = new DistributionServiceInformation
                {
                    FullName = "Plugin", DefaultAlias = "plugin", Version = "1.0.0",
                    Secrets = new List<DistributionSecretInfo>(),
                };
                pluginDownloader.LocalServiceInformationByPath[githubPath] = sameVersionInfo;
                pluginDownloader.LocalServiceInformationByPath[byteshelfPath] = sameVersionInfo;
                pluginDownloader.ServiceInformation = sameVersionInfo;

                pluginUrlResolver.SetPluginUrl("github", "https://example.com/github");
                pluginUrlResolver.SetPluginUrl("byteshelf", "https://example.com/byteshelf");

                DistUpdateCommand command = new DistUpdateCommand(
                    configManager, outputWriter, pluginUrlResolver, pluginDownloader);

                int exitCode = await command.ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                // Should have resolved both plugins
                Assert.Contains(pluginUrlResolver.MethodCalls, c => c == "ResolveAsync:github");
                Assert.Contains(pluginUrlResolver.MethodCalls, c => c == "ResolveAsync:byteshelf");
                Assert.Contains(outputWriter.Messages, m => m.Contains("All plugins are up to date"));
            }
        }
    }
}
