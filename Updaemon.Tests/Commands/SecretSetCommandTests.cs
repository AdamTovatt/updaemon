using Updaemon.Commands;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class SecretSetCommandTests
    {
        [Fact]
        public async Task ExecuteAsync_SetsSecretViaSecretsManager()
        {
            MockSecretsManager secretsManager = new MockSecretsManager();
            SecretSetCommand command = new SecretSetCommand(secretsManager);

            await command.ExecuteAsync("apiKey", "abc123");

            Assert.Contains(secretsManager.MethodCalls, call => call == "SetSecretAsync:apiKey:abc123");
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesExistingSecret()
        {
            MockSecretsManager secretsManager = new MockSecretsManager();
            SecretSetCommand command = new SecretSetCommand(secretsManager);

            await command.ExecuteAsync("apiKey", "oldValue");
            await command.ExecuteAsync("apiKey", "newValue");

            string? value = await secretsManager.GetSecretAsync("apiKey");
            Assert.Equal("newValue", value);
        }
    }
}

