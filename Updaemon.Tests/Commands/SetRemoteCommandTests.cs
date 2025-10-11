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

            await configManager.RegisterServiceAsync("my-api", "MyApi");
            await command.ExecuteAsync("my-api", "UpdatedApi");

            Assert.Contains(configManager.MethodCalls, call => call == "SetRemoteNameAsync:my-api:UpdatedApi");
        }

        [Fact]
        public async Task ExecuteAsync_ThrowsWhenServiceDoesNotExist()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetRemoteCommand command = new SetRemoteCommand(configManager, outputWriter);

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await command.ExecuteAsync("non-existent", "RemoteName")
            );
        }
    }
}

