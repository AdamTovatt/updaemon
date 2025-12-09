using Updaemon.Commands;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class SetExecNameCommandTests
    {
        [Fact]
        public async Task ExecuteAsync_WithValidExecutableName_SetsExecutableName()
        {
            // Arrange
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetExecNameCommand command = new SetExecNameCommand(configManager, outputWriter);

            await configManager.RegisterServiceAsync("test-service", "TestService", "github");

            // Act
            int exitCode = await command.ExecuteAsync(new[] { "test-service", "TestServiceExecutable" });
            Assert.Equal(0, exitCode);

            // Assert
            Assert.Contains("SetExecutableNameAsync:test-service:TestServiceExecutable", configManager.MethodCalls);
            Assert.Contains("Setting executable name for 'test-service' to 'TestServiceExecutable'", outputWriter.Messages);
            Assert.Contains("Executable name updated successfully", outputWriter.Messages);
        }

        [Fact]
        public async Task ExecuteAsync_WithDash_ClearsExecutableName()
        {
            // Arrange
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetExecNameCommand command = new SetExecNameCommand(configManager, outputWriter);

            await configManager.RegisterServiceAsync("test-service", "TestService", "github");

            // Act
            int exitCode = await command.ExecuteAsync(new[] { "test-service", "-" });
            Assert.Equal(0, exitCode);

            // Assert
            Assert.Contains("SetExecutableNameAsync:test-service:null", configManager.MethodCalls);
            Assert.Contains("Clearing executable name for 'test-service' (will use local name)", outputWriter.Messages);
            Assert.Contains("Executable name updated successfully", outputWriter.Messages);
        }

        [Fact]
        public async Task ExecuteAsync_WithNonExistentService_ThrowsException()
        {
            // Arrange
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetExecNameCommand command = new SetExecNameCommand(configManager, outputWriter);

            // Act
            int exitCode = await command.ExecuteAsync(new[] { "non-existent-service", "SomeExecutable" });

            // Assert
            Assert.Equal(1, exitCode);
        }

        [Fact]
        public async Task ExecuteAsync_WithoutRequiredArgs_ReturnsErrorCode()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetExecNameCommand command = new SetExecNameCommand(configManager, outputWriter);

            int exitCode = await command.ExecuteAsync(new[] { "app-name" });

            Assert.Equal(1, exitCode);
            Assert.Contains(outputWriter.Errors, e => e.Contains("Insufficient arguments"));
        }
    }
}

