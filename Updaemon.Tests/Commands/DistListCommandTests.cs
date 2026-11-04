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
        public async Task ExecuteAsync_EachPlugin_SaysHowItIsUpdated()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("github", tempHelper.CreateTempFile("plugins/github/bin", "plugin")));
                await configManager.AddOrUpdatePluginAsync(PluginRecords.FromGitHub("s3", tempHelper.CreateTempFile("plugins/s3/bin", "plugin"), "someone", "updaemon-s3", "v2.1.0"));
                await configManager.AddOrUpdatePluginAsync(PluginRecords.FromUrl("mine", tempHelper.CreateTempFile("plugins/mine/bin", "plugin")));
                MockOutputWriter output = new MockOutputWriter();

                await new DistListCommand(configManager, output, new MockDistributionServiceClient(), AppHarness.RunningVersion).ExecuteAsync(Array.Empty<string>());

                Assert.Equal(
                    new[]
                    {
                        "  Updates: with updaemon",
                        "  Updates: with the latest release of github:someone/updaemon-s3, now v2.1.0",
                        "  Updates: not updatable: installed from a URL, so only installing it again replaces it",
                    },
                    output.Messages.Where(line => line.StartsWith("  Updates: ", StringComparison.Ordinal)));
            }
        }

        [Fact]
        public async Task ExecuteAsync_GitHubPluginWithSelfUpdateOffAndASkippedRelease_SaysBoth()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                UpdaemonConfig config = await configManager.LoadConfigAsync();
                config.SelfUpdate = false;
                await configManager.SaveConfigAsync(config);
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo
                {
                    Alias = "s3",
                    Path = tempHelper.CreateTempFile("plugins/s3/bin", "plugin"),
                    Source = new PluginSource.GitHub("someone", "updaemon-s3", "v2.1.0") { SkippedTag = "v2.2.0" },
                });
                MockOutputWriter output = new MockOutputWriter();

                await new DistListCommand(configManager, output, new MockDistributionServiceClient(), AppHarness.RunningVersion).ExecuteAsync(Array.Empty<string>());

                Assert.Contains(
                    "  Updates: not while self-update is off; now v2.1.0 of github:someone/updaemon-s3; " +
                    "skips v2.2.0, which was refused (retry it with 'updaemon dist-install --as s3 github:someone/updaemon-s3')",
                    output.Messages);
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

