using Updaemon.Commands;
using Updaemon.Models;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class SetRestartCommandTests
    {
        [Fact]
        public async Task ExecuteAsync_Auto_SetsAutoRestartTrueViaConfigManager()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetRestartCommand command = new SetRestartCommand(configManager, outputWriter);

            await configManager.RegisterServiceAsync("my-api", "MyApi", "github");
            int exitCode = await command.ExecuteAsync(new[] { "my-api", "auto" });

            Assert.Equal(0, exitCode);
            Assert.Contains(configManager.MethodCalls, call => call == "SetAutoRestartAsync:my-api:True");

            // A regular service should not get the CLI-only note.
            Assert.DoesNotContain(outputWriter.Messages, m => m.Contains("no effect for CLI tools"));
        }

        [Fact]
        public async Task ExecuteAsync_Manual_SetsAutoRestartFalseViaConfigManager()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetRestartCommand command = new SetRestartCommand(configManager, outputWriter);

            await configManager.RegisterServiceAsync("my-api", "MyApi", "github");
            int exitCode = await command.ExecuteAsync(new[] { "my-api", "manual" });

            Assert.Equal(0, exitCode);
            Assert.Contains(configManager.MethodCalls, call => call == "SetAutoRestartAsync:my-api:False");
        }

        [Fact]
        public async Task ExecuteAsync_ModeIsCaseInsensitive()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetRestartCommand command = new SetRestartCommand(configManager, outputWriter);

            await configManager.RegisterServiceAsync("my-api", "MyApi", "github");
            int exitCode = await command.ExecuteAsync(new[] { "my-api", "MANUAL" });

            Assert.Equal(0, exitCode);
            Assert.Contains(configManager.MethodCalls, call => call == "SetAutoRestartAsync:my-api:False");
        }

        [Fact]
        public async Task ExecuteAsync_InvalidMode_ReturnsErrorCodeAndDoesNotSet()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetRestartCommand command = new SetRestartCommand(configManager, outputWriter);

            await configManager.RegisterServiceAsync("my-api", "MyApi", "github");
            int exitCode = await command.ExecuteAsync(new[] { "my-api", "sometimes" });

            Assert.Equal(1, exitCode);
            Assert.Contains(outputWriter.Errors, e => e.Contains("Invalid restart mode"));
            Assert.DoesNotContain(configManager.MethodCalls, call => call.StartsWith("SetAutoRestartAsync"));
        }

        [Fact]
        public async Task ExecuteAsync_ServiceDoesNotExist_ReturnsErrorCode()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetRestartCommand command = new SetRestartCommand(configManager, outputWriter);

            int exitCode = await command.ExecuteAsync(new[] { "non-existent", "manual" });

            Assert.Equal(1, exitCode);
            Assert.Contains(outputWriter.Errors, e => e.Contains("not registered"));
        }

        [Fact]
        public async Task ExecuteAsync_WithoutRequiredArgs_ReturnsErrorCode()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetRestartCommand command = new SetRestartCommand(configManager, outputWriter);

            int exitCode = await command.ExecuteAsync(new[] { "app-name" });

            Assert.Equal(1, exitCode);
            Assert.Contains(outputWriter.Errors, e => e.Contains("Insufficient arguments"));
        }

        [Fact]
        public async Task ExecuteAsync_CliTool_WarnsSettingHasNoEffect()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            SetRestartCommand command = new SetRestartCommand(configManager, outputWriter);

            await configManager.RegisterServiceAsync("my-tool", "MyTool", "github", ServiceType.Cli);
            int exitCode = await command.ExecuteAsync(new[] { "my-tool", "manual" });

            Assert.Equal(0, exitCode);
            Assert.Contains(outputWriter.Messages, m => m.Contains("no effect for CLI tools"));
        }
    }
}
