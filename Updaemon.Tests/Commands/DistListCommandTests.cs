using Updaemon.Commands;
using Updaemon.Models;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class DistListCommandTests
    {
        [Fact]
        public async Task ExecuteAsync_ListsAllInstalledPlugins_InvokesClientForMetadata()
        {
            MockConfigManager configManager = new MockConfigManager();
            // Add two plugins
            await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = "/plugins/github/bin" });
            await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "byteshelf", Path = "/plugins/byteshelf/bin" });

            MockOutputWriter output = new MockOutputWriter();
            MockDistributionServiceClient client = new MockDistributionServiceClient();

            DistListCommand command = new DistListCommand(configManager, output, client);
            await command.ExecuteAsync();

            // Should connect and request metadata twice
            Assert.Contains(client.MethodCalls, c => c.StartsWith("ConnectAsync:/plugins/github/bin"));
            Assert.Contains(client.MethodCalls, c => c.StartsWith("ConnectAsync:/plugins/byteshelf/bin"));
            Assert.Equal(2, client.MethodCalls.Count(c => c == nameof(client.GetServiceInformationAsync)));

            // Output should include aliases
            Assert.Contains(output.Messages, line => line.Contains("Alias: github"));
            Assert.Contains(output.Messages, line => line.Contains("Alias: byteshelf"));
        }

        [Fact]
        public async Task ExecuteAsync_HandlesMissingPluginFile_Gracefully()
        {
            MockConfigManager configManager = new MockConfigManager();
            // Register plugin with non-existent path
            await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "missing", Path = "/nonexistent/path/bin" });

            MockOutputWriter output = new MockOutputWriter();
            MockDistributionServiceClient client = new MockDistributionServiceClient();

            DistListCommand command = new DistListCommand(configManager, output, client);
            await command.ExecuteAsync();

            // Should not try to connect to missing file
            Assert.DoesNotContain(client.MethodCalls, c => c.StartsWith("ConnectAsync:/nonexistent/path/bin"));

            // Should output error status
            Assert.Contains(output.Messages, line => line.Contains("Alias: missing"));
            Assert.Contains(output.Messages, line => line.Contains("Plugin file not found"));
        }

        [Fact]
        public async Task ExecuteAsync_HandlesMetadataRetrievalError_Gracefully()
        {
            MockConfigManager configManager = new MockConfigManager();
            await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "error-plugin", Path = "/plugins/error/bin" });

            MockOutputWriter output = new MockOutputWriter();
            MockDistributionServiceClient client = new MockDistributionServiceClient();
            // Make GetServiceInformationAsync throw
            client.GetServiceInformationAsyncThrows = true;

            DistListCommand command = new DistListCommand(configManager, output, client);
            await command.ExecuteAsync();

            // Should still output plugin info with error status
            Assert.Contains(output.Messages, line => line.Contains("Alias: error-plugin"));
            Assert.Contains(output.Messages, line => line.Contains("Error retrieving metadata"));
        }
    }
}

