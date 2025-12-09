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
                MockServiceManager serviceManager = new MockServiceManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockUnitFileManager unitFileManager = new MockUnitFileManager
                {
                    TemplateWithSubstitutions = "[Unit]\nDescription=test\n",
                };
                string serviceDirectory = tempHelper.TempDirectory;
                string systemdDirectory = tempHelper.CreateTempDirectory("systemd");

                NewCommand command = new NewCommand(configManager, serviceManager, outputWriter, unitFileManager, serviceDirectory, systemdDirectory);

                // Setup: Add a plugin first
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                int exitCode = await command.ExecuteAsync(new[] { "my-api", "--from", "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(configManager.MethodCalls, call => call.Contains("RegisterServiceAsync:my-api:my-api"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_EnablesServiceViaServiceManager()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockServiceManager serviceManager = new MockServiceManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockUnitFileManager unitFileManager = new MockUnitFileManager
                {
                    TemplateWithSubstitutions = "[Unit]\nDescription=test\n",
                };
                string serviceDirectory = tempHelper.TempDirectory;
                string systemdDirectory = tempHelper.CreateTempDirectory("systemd");

                NewCommand command = new NewCommand(configManager, serviceManager, outputWriter, unitFileManager, serviceDirectory, systemdDirectory);

                // Setup: Add a plugin first
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                int exitCode = await command.ExecuteAsync(new[] { "my-api", "--from", "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(serviceManager.MethodCalls, call => call == "EnableServiceAsync:my-api");
            }
        }

        [Fact]
        public async Task ExecuteAsync_UsesSameNameForLocalAndRemoteInitially()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockServiceManager serviceManager = new MockServiceManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockUnitFileManager unitFileManager = new MockUnitFileManager
                {
                    TemplateWithSubstitutions = "[Unit]\nDescription=test\n",
                };
                string serviceDirectory = tempHelper.TempDirectory;
                string systemdDirectory = tempHelper.CreateTempDirectory("systemd");

                NewCommand command = new NewCommand(configManager, serviceManager, outputWriter, unitFileManager, serviceDirectory, systemdDirectory);

                // Setup: Add a plugin first
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);

                int exitCode = await command.ExecuteAsync(new[] { "test-service", "--from", "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains(configManager.MethodCalls, call => call.Contains("RegisterServiceAsync:test-service:test-service"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginNotFound_ThrowsException()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockServiceManager serviceManager = new MockServiceManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockUnitFileManager unitFileManager = new MockUnitFileManager
                {
                    TemplateWithSubstitutions = "[Unit]\nDescription=test\n",
                };
                string serviceDirectory = tempHelper.TempDirectory;
                string systemdDirectory = tempHelper.CreateTempDirectory("systemd");

                NewCommand command = new NewCommand(configManager, serviceManager, outputWriter, unitFileManager, serviceDirectory, systemdDirectory);

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
                MockServiceManager serviceManager = new MockServiceManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockUnitFileManager unitFileManager = new MockUnitFileManager
                {
                    TemplateWithSubstitutions = "[Unit]\nDescription=test\n",
                };
                string serviceDirectory = tempHelper.TempDirectory;
                string systemdDirectory = tempHelper.CreateTempDirectory("systemd");

                NewCommand command = new NewCommand(configManager, serviceManager, outputWriter, unitFileManager, serviceDirectory, systemdDirectory);

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
                MockServiceManager serviceManager = new MockServiceManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockUnitFileManager unitFileManager = new MockUnitFileManager
                {
                    TemplateWithSubstitutions = "[Unit]\nDescription=test\n",
                };
                string serviceDirectory = tempHelper.TempDirectory;
                string systemdDirectory = tempHelper.CreateTempDirectory("systemd");

                NewCommand command = new NewCommand(configManager, serviceManager, outputWriter, unitFileManager, serviceDirectory, systemdDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "my-service" });

                Assert.Equal(1, exitCode);
                Assert.Contains(outputWriter.Errors, e => e.Contains("Missing required flag"));
            }
        }
    }
}

