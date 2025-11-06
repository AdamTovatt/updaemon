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
            MockOutputWriter outputWriter = new MockOutputWriter();
            SecretSetCommand command = new SecretSetCommand(secretsManager, outputWriter);

            await command.ExecuteAsync("github", "apiKey", "abc123");

            Assert.Contains(secretsManager.MethodCalls, call => call == "SetSecretAsync:github:apiKey:abc123");
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesExistingSecret()
        {
            MockSecretsManager secretsManager = new MockSecretsManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SecretSetCommand command = new SecretSetCommand(secretsManager, outputWriter);

            await command.ExecuteAsync("github", "apiKey", "oldValue");
            await command.ExecuteAsync("github", "apiKey", "newValue");

            string? value = await secretsManager.GetSecretAsync("github", "apiKey");
            Assert.Equal("newValue", value);
        }
    }
}

