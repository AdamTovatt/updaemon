using Updaemon.Commands;
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

                await command.ExecuteAsync("my-api");

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

                await command.ExecuteAsync("my-api");

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

                await command.ExecuteAsync("test-service");

                Assert.Contains(configManager.MethodCalls, call => call == "RegisterServiceAsync:test-service:test-service");
            }
        }
    }
}

