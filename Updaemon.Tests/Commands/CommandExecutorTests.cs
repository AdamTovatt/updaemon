using Updaemon.Commands;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class CommandExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_NoArgs_ReturnsErrorCode()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockOutputWriter outputWriter = new MockOutputWriter();
            CommandExecutor executor = new CommandExecutor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(1, exitCode);
        }

        [Fact]
        public async Task ExecuteAsync_UnknownCommand_ReturnsErrorCode()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockOutputWriter outputWriter = new MockOutputWriter();
            CommandExecutor executor = new CommandExecutor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(new[] { "unknown" });

            Assert.Equal(1, exitCode);
            Assert.Contains("Unknown command: unknown", outputWriter.Errors);
        }

        [Fact]
        public async Task ExecuteAsync_RoutesToCorrectCommand()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockCommand updateCommand = new MockCommand { Name = "update" };
            factory.RegisterCommand("update", updateCommand);
            MockOutputWriter outputWriter = new MockOutputWriter();
            CommandExecutor executor = new CommandExecutor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(new[] { "update", "my-app" });

            Assert.Equal(0, exitCode);
            Assert.Equal(new[] { "my-app" }, updateCommand.ReceivedArgs);
        }

        [Fact]
        public async Task ExecuteAsync_CommandThrowsException_ReturnsErrorCode()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockCommand failingCommand = new MockCommand
            {
                Name = "test",
                ExceptionToThrow = new InvalidOperationException("Test error")
            };
            factory.RegisterCommand("test", failingCommand);
            MockOutputWriter outputWriter = new MockOutputWriter();
            CommandExecutor executor = new CommandExecutor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(new[] { "test" });

            Assert.Equal(1, exitCode);
            Assert.Contains("Error: Test error", outputWriter.Errors);
        }

        [Fact]
        public async Task ExecuteAsync_HelpCommand_ShowsGeneralHelp()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockCommand cmd1 = new MockCommand { Name = "update", Description = "Update services" };
            MockCommand cmd2 = new MockCommand { Name = "new", Description = "Create service" };
            factory.RegisterCommand("update", cmd1);
            factory.RegisterCommand("new", cmd2);
            MockOutputWriter outputWriter = new MockOutputWriter();
            CommandExecutor executor = new CommandExecutor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(new[] { "help" });

            Assert.Equal(0, exitCode);
            Assert.Contains("Available Commands:", outputWriter.Messages);
            Assert.Contains("update", string.Join(" ", outputWriter.Messages));
            Assert.Contains("new", string.Join(" ", outputWriter.Messages));
        }

        [Fact]
        public async Task ExecuteAsync_HelpCommand_ShowsSpecificCommandHelp()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockCommand updateCommand = new MockCommand
            {
                Name = "update",
                DetailedHelp = "Update Command Help"
            };
            factory.RegisterCommand("update", updateCommand);
            MockOutputWriter outputWriter = new MockOutputWriter();
            CommandExecutor executor = new CommandExecutor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(new[] { "help", "update" });

            Assert.Equal(0, exitCode);
            Assert.Contains("Update Command Help", outputWriter.Messages);
        }

        [Fact]
        public async Task ExecuteAsync_HelpCommand_UnknownCommand_ShowsError()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockOutputWriter outputWriter = new MockOutputWriter();
            CommandExecutor executor = new CommandExecutor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(new[] { "help", "unknown" });

            Assert.Equal(1, exitCode);
            Assert.Contains("Unknown command: unknown", outputWriter.Errors);
        }

        [Fact]
        public async Task ExecuteAsync_CaseInsensitiveCommands_Success()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockCommand secretSetCommand = new MockCommand { Name = "secret-set" };
            factory.RegisterCommand("secret-set", secretSetCommand);
            MockOutputWriter outputWriter = new MockOutputWriter();
            CommandExecutor executor = new CommandExecutor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(new[] { "SECRET-SET", "github", "key", "value" });

            Assert.Equal(0, exitCode);
            Assert.Equal(new[] { "github", "key", "value" }, secretSetCommand.ReceivedArgs);
        }
    }
}
