using Updaemon.Commands;
using Updaemon.Configuration;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class CommandExecutorTests
    {
        private static readonly UpdaemonVersion RunningVersion = UpdaemonVersion.FromAssembly(new Version(1, 2, 0, 0));

        private static CommandExecutor Executor(MockCommandFactory factory, MockOutputWriter outputWriter, MockRunLock? runLock = null, MockZeroXConverter? converter = null) =>
            new CommandExecutor(factory, outputWriter, runLock ?? new MockRunLock(), RunningVersion, converter ?? new MockZeroXConverter());

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_ZeroXSetup_IsConvertedOnceUnderTheLockBeforeTheCommand(bool changesState)
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockRunLock runLock = new MockRunLock();
            bool heldWhileConverting = false;
            MockZeroXConverter converter = new MockZeroXConverter { Pending = true, OnRun = () => heldWhileConverting = runLock.IsHeld };
            bool convertedFirst = false;
            factory.RegisterCommand("cmd", new MockCommand { Name = "cmd", ChangesState = changesState, OnExecute = () => convertedFirst = converter.Runs.Count == 1 });
            CommandExecutor executor = Executor(factory, new MockOutputWriter(), runLock, converter);

            await executor.ExecuteAsync(new[] { "cmd" });
            await executor.ExecuteAsync(new[] { "cmd" });

            Assert.True(convertedFirst);
            Assert.True(heldWhileConverting);
            Assert.Equal(new[] { false }, converter.Runs);
            Assert.Equal(changesState ? 2 : 1, runLock.Acquisitions);
        }

        [Fact]
        public async Task ExecuteAsync_ZeroXSetupConvertedByAnotherUpdaemonWhileWaitingForTheLock_IsNotConvertedAgain()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockZeroXConverter converter = new MockZeroXConverter { Pending = true };
            MockRunLock runLock = new MockRunLock { OnAcquire = () => converter.Pending = false };
            factory.RegisterCommand("status", new MockCommand { Name = "status", ChangesState = false });

            int exitCode = await Executor(factory, new MockOutputWriter(), runLock, converter).ExecuteAsync(new[] { "status" });

            Assert.Equal(0, exitCode);
            Assert.Empty(converter.Runs);
        }

        [Fact]
        public async Task ExecuteAsync_ConversionLeavesSomethingOut_StillRunsTheCommand()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockCommand command = new MockCommand { Name = "status", ChangesState = false };
            factory.RegisterCommand("status", command);
            MockZeroXConverter converter = new MockZeroXConverter { Pending = true, Result = false };

            int exitCode = await Executor(factory, new MockOutputWriter(), converter: converter).ExecuteAsync(new[] { "status", "x" });

            Assert.Equal(0, exitCode);
            Assert.Equal(new[] { "x" }, command.ReceivedArgs);
        }

        [Fact]
        public async Task ExecuteAsync_ConversionFails_ReportsItAndDoesNotRunTheCommand()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockCommand command = new MockCommand { Name = "status", ChangesState = false };
            factory.RegisterCommand("status", command);
            MockOutputWriter output = new MockOutputWriter();
            MockZeroXConverter converter = new MockZeroXConverter { Pending = true, Failure = new IOException("disk full") };

            int exitCode = await Executor(factory, output, converter: converter).ExecuteAsync(new[] { "status" });

            Assert.Equal(1, exitCode);
            Assert.Equal(new[] { "Error: disk full" }, output.Errors);
            Assert.Null(command.ReceivedArgs);
        }

        [Theory]
        [InlineData("--version")]
        [InlineData("help")]
        [InlineData("migrate")]
        public async Task ExecuteAsync_ZeroXSetup_IsNotConvertedByVersionHelpOrMigrateItself(string commandName)
        {
            MockCommandFactory factory = new MockCommandFactory();
            factory.RegisterCommand("migrate", new MigrateCommand(new MockZeroXConverter(), new MockOutputWriter(), TestScope.Create("/nonexistent", isSystem: true)));
            MockZeroXConverter converter = new MockZeroXConverter { Pending = true };

            await Executor(factory, new MockOutputWriter(), converter: converter).ExecuteAsync(new[] { commandName });

            Assert.Empty(converter.Runs);
        }

        [Fact]
        public async Task ExecuteAsync_VersionFlag_PrintsTheReleaseVersionOnly()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockOutputWriter outputWriter = new MockOutputWriter();
            CommandExecutor executor = Executor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(new[] { "--version" });

            Assert.Equal(0, exitCode);
            Assert.Equal(new[] { "1.2.0" }, outputWriter.Messages);
        }

        [Fact]
        public async Task ExecuteAsync_VersionFlagWithAnArgument_ShowsItsUsage()
        {
            MockOutputWriter outputWriter = new MockOutputWriter();

            int exitCode = await Executor(new MockCommandFactory(), outputWriter).ExecuteAsync(new[] { "--version", "x" });

            Assert.Equal(1, exitCode);
            Assert.Equal(new[] { "Usage: updaemon --version" }, outputWriter.Errors);
        }

        [Fact]
        public async Task ExecuteAsync_NoArgs_ReturnsErrorCode()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockOutputWriter outputWriter = new MockOutputWriter();
            CommandExecutor executor = Executor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(1, exitCode);
        }

        [Fact]
        public async Task ExecuteAsync_UnknownCommand_ReturnsErrorCode()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockOutputWriter outputWriter = new MockOutputWriter();
            CommandExecutor executor = Executor(factory, outputWriter);

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
            CommandExecutor executor = Executor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(new[] { "update", "my-app" });

            Assert.Equal(0, exitCode);
            Assert.Equal(new[] { "my-app" }, updateCommand.ReceivedArgs);
        }

        [Theory]
        [InlineData(true, 1)]
        [InlineData(false, 0)]
        public async Task ExecuteAsync_HoldsTheLockOnlyForCommandsThatChangeState(bool changesState, int expectedAcquisitions)
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockRunLock runLock = new MockRunLock();
            bool heldDuringRun = false;
            MockCommand command = new MockCommand { Name = "cmd", ChangesState = changesState, OnExecute = () => heldDuringRun = runLock.IsHeld };
            factory.RegisterCommand("cmd", command);
            CommandExecutor executor = Executor(factory, new MockOutputWriter(), runLock);

            await executor.ExecuteAsync(new[] { "cmd" });

            Assert.Equal(expectedAcquisitions, runLock.Acquisitions);
            Assert.Equal(changesState, heldDuringRun);
            Assert.False(runLock.IsHeld);
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
            CommandExecutor executor = Executor(factory, outputWriter);

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
            CommandExecutor executor = Executor(factory, outputWriter);

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
            CommandExecutor executor = Executor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(new[] { "help", "update" });

            Assert.Equal(0, exitCode);
            Assert.Contains("Update Command Help", outputWriter.Messages);
        }

        [Fact]
        public async Task ExecuteAsync_HelpCommand_UnknownCommand_ShowsError()
        {
            MockCommandFactory factory = new MockCommandFactory();
            MockOutputWriter outputWriter = new MockOutputWriter();
            CommandExecutor executor = Executor(factory, outputWriter);

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
            CommandExecutor executor = Executor(factory, outputWriter);

            int exitCode = await executor.ExecuteAsync(new[] { "SECRET-SET", "github", "key", "value" });

            Assert.Equal(0, exitCode);
            Assert.Equal(new[] { "github", "key", "value" }, secretSetCommand.ReceivedArgs);
        }
    }
}
