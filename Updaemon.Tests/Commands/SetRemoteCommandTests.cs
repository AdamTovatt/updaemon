using Updaemon.Commands;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class SetRemoteCommandTests
    {
        [Fact]
        public async Task ExecuteAsync_UpdatesRemoteNameViaConfigManager()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetRemoteCommand command = new SetRemoteCommand(configManager, outputWriter);

            await configManager.RegisterServiceAsync("my-api", "MyApi", "github");
            int exitCode = await command.ExecuteAsync(new[] { "my-api", "UpdatedApi" });

            Assert.Equal(0, exitCode);
            Assert.Contains(configManager.MethodCalls, call => call == "SetRemoteNameAsync:my-api:UpdatedApi");
        }

        [Fact]
        public async Task ExecuteAsync_ThrowsWhenServiceDoesNotExist()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetRemoteCommand command = new SetRemoteCommand(configManager, outputWriter);

            int exitCode = await command.ExecuteAsync(new[] { "non-existent", "RemoteName" });

            Assert.Equal(1, exitCode);
        }

        [Fact]
        public async Task ExecuteAsync_WithoutRequiredArgs_ReturnsErrorCode()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetRemoteCommand command = new SetRemoteCommand(configManager, outputWriter);

            int exitCode = await command.ExecuteAsync(new[] { "app-name" });

            Assert.Equal(1, exitCode);
            Assert.Contains(outputWriter.Errors, e => e.Contains("Insufficient arguments"));
        }
    }
}

