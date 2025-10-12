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

            await configManager.RegisterServiceAsync("test-service", "TestService");

            // Act
            await command.ExecuteAsync("test-service", "TestServiceExecutable");

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

            await configManager.RegisterServiceAsync("test-service", "TestService");

            // Act
            await command.ExecuteAsync("test-service", "-");

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

            // Act & Assert
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => command.ExecuteAsync("non-existent-service", "SomeExecutable"));

            Assert.Contains("not registered", exception.Message);
        }
    }
}

