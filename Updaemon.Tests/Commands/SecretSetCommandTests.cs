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

            int exitCode = await command.ExecuteAsync(new[] { "github", "apiKey", "abc123" });
            Assert.Equal(0, exitCode);

            Assert.Contains(secretsManager.MethodCalls, call => call == "SetSecretAsync:github:apiKey:abc123");
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesExistingSecret()
        {
            MockSecretsManager secretsManager = new MockSecretsManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SecretSetCommand command = new SecretSetCommand(secretsManager, outputWriter);

            int exitCode1 = await command.ExecuteAsync(new[] { "github", "apiKey", "oldValue" });
            int exitCode2 = await command.ExecuteAsync(new[] { "github", "apiKey", "newValue" });
            Assert.Equal(0, exitCode1);
            Assert.Equal(0, exitCode2);

            string? value = await secretsManager.GetSecretAsync("github", "apiKey");
            Assert.Equal("newValue", value);
        }

        [Fact]
        public async Task ExecuteAsync_WithoutRequiredArgs_ReturnsErrorCode()
        {
            MockSecretsManager secretsManager = new MockSecretsManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SecretSetCommand command = new SecretSetCommand(secretsManager, outputWriter);

            int exitCode = await command.ExecuteAsync(new[] { "plugin", "key" });

            Assert.Equal(1, exitCode);
            Assert.Contains(outputWriter.Errors, e => e.Contains("Insufficient arguments"));
        }
    }
}

