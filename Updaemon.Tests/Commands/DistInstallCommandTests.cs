using Updaemon.Commands;
using Updaemon.Common.Models;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class DistInstallCommandTests
    {
        [Fact]
        public async Task ExecuteAsync_UpdatesConfigWithPluginPath()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader();
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");

                DistInstallCommand command = new DistInstallCommand(configManager, new MockOutputWriter(), new MockPluginUrlResolver(), pluginDownloader, pluginsDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "https://example.com/plugins/my-plugin" });
                Assert.Equal(0, exitCode);

                Assert.Contains(configManager.MethodCalls, call => call.StartsWith("AddOrUpdatePluginAsync:"));
                IReadOnlyDictionary<string, InstalledPluginInfo> plugins = await configManager.GetAllPluginsAsync();
                Assert.NotEmpty(plugins);
                Assert.Contains("my-plugin", plugins.Values.First().Path);
            }
        }

        [Fact]
        public async Task ExecuteAsync_UsesProvidedAlias_WhenAsSpecified()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader();
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");

                DistInstallCommand command = new DistInstallCommand(configManager, new MockOutputWriter(), new MockPluginUrlResolver(), pluginDownloader, pluginsDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "--as", "github", "https://example.com/path/to/plugin-bin" });
                Assert.Equal(0, exitCode);

                IReadOnlyDictionary<string, InstalledPluginInfo> plugins = await configManager.GetAllPluginsAsync();
                Assert.True(plugins.ContainsKey("github"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_UsesDefaultAlias_WhenAliasNotProvided()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                // MockPluginDownloader returns DefaultAlias = "mock" by default
                MockPluginDownloader pluginDownloader = new MockPluginDownloader();
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");

                DistInstallCommand command = new DistInstallCommand(configManager, new MockOutputWriter(), new MockPluginUrlResolver(), pluginDownloader, pluginsDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "https://example.com/path/to/plugin-bin" });
                Assert.Equal(0, exitCode);

                IReadOnlyDictionary<string, InstalledPluginInfo> plugins = await configManager.GetAllPluginsAsync();
                Assert.True(plugins.ContainsKey("mock"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_Throws_WhenAliasAlreadyExists()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                // Pre-register alias
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "dup", Path = "/existing/path" });

                MockPluginDownloader pluginDownloader = new MockPluginDownloader();
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");

                DistInstallCommand command = new DistInstallCommand(configManager, new MockOutputWriter(), new MockPluginUrlResolver(), pluginDownloader, pluginsDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "--as", "dup", "https://example.com/path/to/plugin-bin" });
                Assert.Equal(1, exitCode);
            }
        }

        [Fact]
        public async Task ExecuteAsync_DoesNotDownload_WhenExplicitAliasAlreadyExists()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                // Pre-register alias
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = "/existing/path" });

                MockPluginDownloader pluginDownloader = new MockPluginDownloader();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");

                DistInstallCommand command = new DistInstallCommand(configManager, outputWriter, new MockPluginUrlResolver(), pluginDownloader, pluginsDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "--as", "github", "https://example.com/path/to/plugin-bin" });
                Assert.Equal(1, exitCode);

                // Verify that the downloader was never called (no download occurred)
                Assert.Empty(pluginDownloader.MethodCalls);
                Assert.Contains(outputWriter.Errors, e => e.Contains("already installed"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_HandlesDownloadFailure()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader();
                pluginDownloader.ExceptionToThrow = new HttpRequestException("Network error");
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");

                DistInstallCommand command = new DistInstallCommand(configManager, new MockOutputWriter(), new MockPluginUrlResolver(), pluginDownloader, pluginsDirectory);

                await Assert.ThrowsAsync<HttpRequestException>(
                    async () => await command.ExecuteAsync(new[] { "https://example.com/invalid-plugin" })
                );
            }
        }

        [Fact]
        public async Task ExecuteAsync_ExtractsFilenameFromUrl()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader();
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");

                DistInstallCommand command = new DistInstallCommand(configManager, new MockOutputWriter(), new MockPluginUrlResolver(), pluginDownloader, pluginsDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "https://example.com/path/to/byteshelf-dist" });
                Assert.Equal(0, exitCode);

                IReadOnlyDictionary<string, InstalledPluginInfo> plugins = await configManager.GetAllPluginsAsync();
                Assert.NotEmpty(plugins);
                Assert.Contains("byteshelf-dist", plugins.Values.First().Path);
            }
        }

        [Fact]
        public async Task ExecuteAsync_Throws_WhenPluginHasEmptyDefaultAliasAndNoAliasProvided()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockPluginDownloader pluginDownloader = new MockPluginDownloader();
                pluginDownloader.ServiceInformation = new DistributionServiceInformation
                {
                    FullName = "Test Plugin",
                    DefaultAlias = "", // Empty alias
                    Description = "Test",
                    Version = "1.0.0",
                    Secrets = new List<DistributionSecretInfo>()
                };
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");

                MockOutputWriter outputWriter = new MockOutputWriter();
                DistInstallCommand command = new DistInstallCommand(configManager, outputWriter, new MockPluginUrlResolver(), pluginDownloader, pluginsDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "https://example.com/path/to/plugin-bin" });
                Assert.Equal(1, exitCode);
                Assert.Contains(outputWriter.Errors, e => e.Contains("does not provide a default alias"));
            }
        }
    }
}
