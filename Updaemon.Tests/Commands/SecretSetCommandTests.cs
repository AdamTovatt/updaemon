using Updaemon.Commands;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class SecretSetCommandTests
    {
        private readonly MockSecretsManager _secretsManager = new MockSecretsManager();
        private readonly MockOutputWriter _outputWriter = new MockOutputWriter();
        private readonly MockPrompter _prompter = new MockPrompter();

        private SecretSetCommand Command() => new SecretSetCommand(_secretsManager, _outputWriter, _prompter);

        [Fact]
        public async Task ExecuteAsync_SetsSecretViaSecretsManager()
        {
            int exitCode = await Command().ExecuteAsync(new[] { "github", "apiKey", "abc123" });

            Assert.Equal(0, exitCode);
            Assert.Contains(_secretsManager.MethodCalls, call => call == "SetSecretAsync:github:apiKey:abc123");
            Assert.Empty(_prompter.Asked);
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesExistingSecret()
        {
            int exitCode1 = await Command().ExecuteAsync(new[] { "github", "apiKey", "oldValue" });
            int exitCode2 = await Command().ExecuteAsync(new[] { "github", "apiKey", "newValue" });

            Assert.Equal(0, exitCode1);
            Assert.Equal(0, exitCode2);
            Assert.Equal("newValue", await _secretsManager.GetSecretAsync("github", "apiKey"));
        }

        [Fact]
        public async Task ExecuteAsync_NoValue_AsksForItWithoutEcho()
        {
            _prompter.Answers.Enqueue(" typed-token ");

            int exitCode = await Command().ExecuteAsync(new[] { "github", "githubToken" });

            Assert.Equal(0, exitCode);
            Assert.Equal(new[] { ("githubToken for the 'github' plugin (not shown): ", true) }, _prompter.Asked);
            Assert.Equal("typed-token", await _secretsManager.GetSecretAsync("github", "githubToken"));
        }

        [Fact]
        public async Task ExecuteAsync_NoValueAndAnEmptyAnswer_ChangesNothing()
        {
            _prompter.Answers.Enqueue("");

            int exitCode = await Command().ExecuteAsync(new[] { "github", "githubToken" });

            Assert.Equal(1, exitCode);
            Assert.Equal(new[] { "Error: No value was given; githubToken is unchanged." }, _outputWriter.Errors);
            Assert.Null(await _secretsManager.GetSecretAsync("github", "githubToken"));
        }

        [Fact]
        public async Task ExecuteAsync_NoValueAndNoTerminal_FailsNamingTheForm()
        {
            _prompter.IsInteractive = false;

            int exitCode = await Command().ExecuteAsync(new[] { "github", "githubToken" });

            Assert.Equal(1, exitCode);
            Assert.Equal(new[] { "Error: No value for githubToken, and no terminal to ask for it. Give it after the key: updaemon secret-set <plugin-alias> <key> [<value>]" }, _outputWriter.Errors);
        }

        [Theory]
        [InlineData("plugin")]
        [InlineData("plugin", "key", "value", "extra")]
        public async Task ExecuteAsync_WrongArgumentCount_ShowsTheUsage(params string[] args)
        {
            SecretSetCommand command = Command();

            int exitCode = await command.ExecuteAsync(args);

            Assert.Equal(1, exitCode);
            Assert.Contains($"Usage: {command.Usage}", _outputWriter.Errors);
        }
    }
}
