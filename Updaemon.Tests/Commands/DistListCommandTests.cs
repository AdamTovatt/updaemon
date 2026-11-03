using Updaemon.Commands;
using Updaemon.Configuration;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class DistListCommandTests
    {
        [Fact]
        public async Task ExecuteAsync_RegistryPluginFromAnotherRelease_IsListedWithoutRunningIt()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string path = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("github", path, UpdaemonVersion.Of(0, 9, 0)));
                MockOutputWriter output = new MockOutputWriter();
                MockDistributionServiceClient client = new MockDistributionServiceClient();

                await new DistListCommand(configManager, output, client, AppHarness.RunningVersion).ExecuteAsync(Array.Empty<string>());

                Assert.Empty(client.MethodCalls);
                Assert.Contains("  Status: from updaemon 0.9.0's release; the next install or update replaces it with this version's build", output.Messages);
            }
        }

        [Fact]
        public async Task ExecuteAsync_ListsAllInstalledPlugins_InvokesClientForMetadata()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                // Add two plugins with actual files
                string githubPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                string byteshelfPath = tempHelper.CreateTempFile("plugins/byteshelf/bin", "fake-plugin");
                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("github", githubPath));
                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("byteshelf", byteshelfPath));

                MockOutputWriter output = new MockOutputWriter();
                MockDistributionServiceClient client = new MockDistributionServiceClient();

                DistListCommand command = new DistListCommand(configManager, output, client, AppHarness.RunningVersion);
                int exitCode = await command.ExecuteAsync(Array.Empty<string>());
                Assert.Equal(0, exitCode);

                // An alias is printed only after connecting to its plugin succeeds
                Assert.Contains(output.Messages, line => line.Contains("Alias: github"));
                Assert.Contains(output.Messages, line => line.Contains("Alias: byteshelf"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_HandlesMissingPluginFile_Gracefully()
        {
            MockConfigManager configManager = new MockConfigManager();
            // Register plugin with non-existent path
            await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("missing", "/nonexistent/path/bin"));

            MockOutputWriter output = new MockOutputWriter();
            MockDistributionServiceClient client = new MockDistributionServiceClient();

            DistListCommand command = new DistListCommand(configManager, output, client, AppHarness.RunningVersion);
            int exitCode = await command.ExecuteAsync(Array.Empty<string>());
            Assert.Equal(0, exitCode);

            // Should not try to connect to missing file
            Assert.DoesNotContain(client.MethodCalls, c => c.StartsWith("ConnectAsync:/nonexistent/path/bin"));

            // Should output error status
            Assert.Contains(output.Messages, line => line.Contains("Alias: missing"));
            Assert.Contains(output.Messages, line => line.Contains("Plugin file not found"));
        }

        [Fact]
        public async Task ExecuteAsync_HandlesMetadataRetrievalError_Gracefully()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/error/bin", "fake-plugin");
                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("error-plugin", pluginPath));

                MockOutputWriter output = new MockOutputWriter();
                MockDistributionServiceClient client = new MockDistributionServiceClient();
                client.ConnectAsyncThrowsForPluginPaths.Add(pluginPath);

                DistListCommand command = new DistListCommand(configManager, output, client, AppHarness.RunningVersion);
                int exitCode = await command.ExecuteAsync(Array.Empty<string>());
                Assert.Equal(0, exitCode);

                // Should still output plugin info with error status
                Assert.Contains(output.Messages, line => line.Contains("Alias: error-plugin"));
                Assert.Contains(output.Messages, line => line.Contains("Error retrieving metadata"));
            }
        }
    }
}

