using Updaemon.Commands;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class SetExecNameCommandTests
    {
        private static MockUnitFileManager NewUnitFileManager(string unitFileDirectory)
        {
            return new MockUnitFileManager { UnitFileDirectory = unitFileDirectory };
        }

        [Fact]
        public async Task ExecuteAsync_WithValidExecutableName_SetsExecutableName()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string systemdDirectory = tempHelper.CreateTempDirectory("systemd");
                MockUnitFileManager unitFileManager = NewUnitFileManager(systemdDirectory);
                MockServiceManager serviceManager = new MockServiceManager();
                string serviceDirectory = tempHelper.TempDirectory;

                SetExecNameCommand command = new SetExecNameCommand(
                    configManager, outputWriter, unitFileManager, serviceManager, serviceDirectory);

                await configManager.RegisterServiceAsync("test-service", "TestService", "github");

                int exitCode = await command.ExecuteAsync(new[] { "test-service", "TestServiceExecutable" });
                Assert.Equal(0, exitCode);

                Assert.Contains("SetExecutableNameAsync:test-service:TestServiceExecutable", configManager.MethodCalls);
                Assert.Contains("Setting executable name for 'test-service' to 'TestServiceExecutable'", outputWriter.Messages);
                Assert.Contains("Executable name updated successfully", outputWriter.Messages);
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithDash_ClearsExecutableName()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string systemdDirectory = tempHelper.CreateTempDirectory("systemd");
                MockUnitFileManager unitFileManager = NewUnitFileManager(systemdDirectory);
                MockServiceManager serviceManager = new MockServiceManager();
                string serviceDirectory = tempHelper.TempDirectory;

                SetExecNameCommand command = new SetExecNameCommand(
                    configManager, outputWriter, unitFileManager, serviceManager, serviceDirectory);

                await configManager.RegisterServiceAsync("test-service", "TestService", "github");

                int exitCode = await command.ExecuteAsync(new[] { "test-service", "-" });
                Assert.Equal(0, exitCode);

                Assert.Contains("SetExecutableNameAsync:test-service:null", configManager.MethodCalls);
                Assert.Contains("Clearing executable name for 'test-service' (will use local name)", outputWriter.Messages);
                Assert.Contains("Executable name updated successfully", outputWriter.Messages);
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithNonExistentService_ReturnsError()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string systemdDirectory = tempHelper.CreateTempDirectory("systemd");
                MockUnitFileManager unitFileManager = NewUnitFileManager(systemdDirectory);
                MockServiceManager serviceManager = new MockServiceManager();
                string serviceDirectory = tempHelper.TempDirectory;

                SetExecNameCommand command = new SetExecNameCommand(
                    configManager, outputWriter, unitFileManager, serviceManager, serviceDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "non-existent-service", "SomeExecutable" });

                Assert.Equal(1, exitCode);
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithoutRequiredArgs_ReturnsErrorCode()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string systemdDirectory = tempHelper.CreateTempDirectory("systemd");
                MockUnitFileManager unitFileManager = NewUnitFileManager(systemdDirectory);
                MockServiceManager serviceManager = new MockServiceManager();
                string serviceDirectory = tempHelper.TempDirectory;

                SetExecNameCommand command = new SetExecNameCommand(
                    configManager, outputWriter, unitFileManager, serviceManager, serviceDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "app-name" });

                Assert.Equal(1, exitCode);
                Assert.Contains(outputWriter.Errors, e => e.Contains("Insufficient arguments"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_WhenUnitFileExists_RegeneratesUnitFile()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string systemdDirectory = tempHelper.CreateTempDirectory("systemd");
                MockUnitFileManager unitFileManager = NewUnitFileManager(systemdDirectory);
                unitFileManager.TemplateWithSubstitutions = "[Unit]\nDescription=test\nExecStart=updated\n";
                MockServiceManager serviceManager = new MockServiceManager();
                string serviceDirectory = tempHelper.TempDirectory;

                SetExecNameCommand command = new SetExecNameCommand(
                    configManager, outputWriter, unitFileManager, serviceManager, serviceDirectory);

                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                // Create an existing unit file to simulate an initialized service
                string unitFilePath = unitFileManager.GetUnitFilePath("my-api");
                await File.WriteAllTextAsync(unitFilePath, "[Unit]\nDescription=old\n");

                int exitCode = await command.ExecuteAsync(new[] { "my-api", "NewExecutable" });
                Assert.Equal(0, exitCode);

                // Unit file should have been rewritten
                string updatedContent = await File.ReadAllTextAsync(unitFilePath);
                Assert.Equal("[Unit]\nDescription=test\nExecStart=updated\n", updatedContent);
                Assert.Contains(outputWriter.Messages, m => m.Contains("Updated service unit file"));
                Assert.Contains(serviceManager.MethodCalls, c => c == "DaemonReloadAsync");
            }
        }

        [Fact]
        public async Task ExecuteAsync_WhenNoUnitFile_DoesNotAttemptRegeneration()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                string systemdDirectory = tempHelper.CreateTempDirectory("systemd");
                MockUnitFileManager unitFileManager = NewUnitFileManager(systemdDirectory);
                MockServiceManager serviceManager = new MockServiceManager();
                string serviceDirectory = tempHelper.TempDirectory;

                SetExecNameCommand command = new SetExecNameCommand(
                    configManager, outputWriter, unitFileManager, serviceManager, serviceDirectory);

                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                // No unit file exists

                int exitCode = await command.ExecuteAsync(new[] { "my-api", "NewExecutable" });
                Assert.Equal(0, exitCode);

                // Should not mention unit file update or reload
                Assert.DoesNotContain(outputWriter.Messages, m => m.Contains("Updated service unit file"));
                Assert.DoesNotContain(serviceManager.MethodCalls, c => c == "DaemonReloadAsync");
            }
        }
    }
}
