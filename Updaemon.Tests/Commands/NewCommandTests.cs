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

                await command.ExecuteAsync("my-api", "github");

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

                await command.ExecuteAsync("my-api", "github");

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

                await command.ExecuteAsync("test-service", "github");

                Assert.Contains(configManager.MethodCalls, call => call.Contains("RegisterServiceAsync:test-service:test-service"));
            }
        }
    }
}

