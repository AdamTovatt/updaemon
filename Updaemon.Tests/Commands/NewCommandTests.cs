using Updaemon.Commands;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class NewCommandTests
    {
        [Fact]
        public async Task ExecuteAsync_RegistersServiceWithConfigManager()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string serviceDirectory = tempHelper.TempDirectory;

                NewCommand command = new NewCommand(configManager, outputWriter, serviceDirectory);

                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                int exitCode = await command.ExecuteAsync(new[] { "my-api", "--from", "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(configManager.MethodCalls, call => call.Contains("RegisterServiceAsync:my-api:my-api"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_DoesNotCreateUnitFileOrEnableService()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string serviceDirectory = tempHelper.TempDirectory;

                NewCommand command = new NewCommand(configManager, outputWriter, serviceDirectory);

                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                int exitCode = await command.ExecuteAsync(new[] { "my-api", "--from", "github" });

                Assert.Equal(0, exitCode);
                // No unit file should be created anywhere in the service directory
                string[] serviceFiles = Directory.GetFiles(Path.Combine(serviceDirectory, "my-api"), "*", SearchOption.AllDirectories);
                Assert.Empty(serviceFiles);
            }
        }

        [Fact]
        public async Task ExecuteAsync_UsesSameNameForLocalAndRemoteByDefault()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string serviceDirectory = tempHelper.TempDirectory;

                NewCommand command = new NewCommand(configManager, outputWriter, serviceDirectory);

                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                int exitCode = await command.ExecuteAsync(new[] { "test-service", "--from", "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(configManager.MethodCalls, call => call.Contains("RegisterServiceAsync:test-service:test-service"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithRemoteFlag_UsesRemoteNameForRegistration()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string serviceDirectory = tempHelper.TempDirectory;

                NewCommand command = new NewCommand(configManager, outputWriter, serviceDirectory);

                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                int exitCode = await command.ExecuteAsync(new[] { "my-api", "--from", "github", "--remote", "owner/repo" });

                Assert.Equal(0, exitCode);
                Assert.Contains(configManager.MethodCalls, call => call.Contains("RegisterServiceAsync:my-api:owner/repo"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_OutputMentionsInitCommand()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string serviceDirectory = tempHelper.TempDirectory;

                NewCommand command = new NewCommand(configManager, outputWriter, serviceDirectory);

                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                int exitCode = await command.ExecuteAsync(new[] { "my-api", "--from", "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(outputWriter.Messages, m => m.Contains("updaemon init"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginNotFound_ReturnsError()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string serviceDirectory = tempHelper.TempDirectory;

                NewCommand command = new NewCommand(configManager, outputWriter, serviceDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "my-service", "--from", "non-existent-plugin" });

                Assert.Equal(1, exitCode);
                Assert.Contains(outputWriter.Errors, e => e.Contains("not installed"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithoutAppName_ReturnsErrorCode()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string serviceDirectory = tempHelper.TempDirectory;

                NewCommand command = new NewCommand(configManager, outputWriter, serviceDirectory);

                int exitCode = await command.ExecuteAsync(Array.Empty<string>());

                Assert.Equal(1, exitCode);
                Assert.Contains(outputWriter.Errors, e => e.Contains("Missing required argument"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithoutFromFlag_ReturnsErrorCode()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string serviceDirectory = tempHelper.TempDirectory;

                NewCommand command = new NewCommand(configManager, outputWriter, serviceDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "my-service" });

                Assert.Equal(1, exitCode);
                Assert.Contains(outputWriter.Errors, e => e.Contains("Missing required flag"));
            }
        }
    }
}
