using Updaemon.Commands;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class CommandExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_NoArgs_ReturnsErrorCode()
        {
            CommandExecutor executor = CreateCommandExecutor();

            int exitCode = await executor.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(1, exitCode);
        }

        [Fact]
        public async Task ExecuteAsync_UnknownCommand_ReturnsErrorCode()
        {
            CommandExecutor executor = CreateCommandExecutor();

            int exitCode = await executor.ExecuteAsync(new[] { "unknown" });

            Assert.Equal(1, exitCode);
        }

        [Fact]
        public async Task ExecuteAsync_NewCommand_WithoutAppName_ReturnsErrorCode()
        {
            CommandExecutor executor = CreateCommandExecutor();

            int exitCode = await executor.ExecuteAsync(new[] { "new" });

            Assert.Equal(1, exitCode);
        }

        [Fact]
        public async Task ExecuteAsync_SetRemoteCommand_WithoutRequiredArgs_ReturnsErrorCode()
        {
            CommandExecutor executor = CreateCommandExecutor();

            int exitCodeWithOneArg = await executor.ExecuteAsync(new[] { "set-remote", "app-name" });
            int exitCodeWithNoArgs = await executor.ExecuteAsync(new[] { "set-remote" });

            Assert.Equal(1, exitCodeWithOneArg);
            Assert.Equal(1, exitCodeWithNoArgs);
        }

        [Fact]
        public async Task ExecuteAsync_DistInstallCommand_WithoutUrl_ReturnsErrorCode()
        {
            CommandExecutor executor = CreateCommandExecutor();

            int exitCode = await executor.ExecuteAsync(new[] { "dist-install" });

            Assert.Equal(1, exitCode);
        }

        [Fact]
        public async Task ExecuteAsync_SecretSetCommand_WithoutKeyValue_ReturnsErrorCode()
        {
            CommandExecutor executor = CreateCommandExecutor();

            int exitCodeWithOneArg = await executor.ExecuteAsync(new[] { "secret-set", "key" });
            int exitCodeWithNoArgs = await executor.ExecuteAsync(new[] { "secret-set" });

            Assert.Equal(1, exitCodeWithOneArg);
            Assert.Equal(1, exitCodeWithNoArgs);
        }

        [Fact]
        public async Task ExecuteAsync_CommandThrowsException_ReturnsErrorCode()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockSecretsManager secretsManager = new MockSecretsManager();
            MockServiceManager serviceManager = new MockServiceManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            MockUnitFileManager unitFileManager = new MockUnitFileManager
            {
                TemplateWithSubstitutions = "[Unit]\nDescription=test\n",
            };

            // Create a command executor with a mock that will throw
            NewCommand newCommand = new NewCommand(configManager, serviceManager, outputWriter, unitFileManager);
            UpdateCommand updateCommand = new UpdateCommand(
                configManager,
                secretsManager,
                serviceManager,
                new MockSymlinkManager(),
                new MockExecutableDetector(),
                new MockDistributionServiceClient(),
                outputWriter,
                new MockVersionExtractor(),
                new MockFilePermissionManager()
            );
            SetRemoteCommand setRemoteCommand = new SetRemoteCommand(configManager, outputWriter);
            SetExecNameCommand setExecNameCommand = new SetExecNameCommand(configManager, outputWriter);
            DistInstallCommand distInstallCommand = new DistInstallCommand(configManager, new HttpClient(), outputWriter);
            SecretSetCommand secretSetCommand = new SecretSetCommand(secretsManager, outputWriter);

            TimerCommand timerCommand = new TimerCommand(new MockTimerManager(), outputWriter);

            CommandExecutor executor = new CommandExecutor(
                newCommand,
                updateCommand,
                setRemoteCommand,
                setExecNameCommand,
                distInstallCommand,
                secretSetCommand,
                timerCommand,
                outputWriter
            );

            // Trying to set remote for non-existent service will throw
            int exitCode = await executor.ExecuteAsync(new[] { "set-remote", "non-existent", "Remote" });

            Assert.Equal(1, exitCode);
        }

        [Fact]
        public async Task ExecuteAsync_CaseInsensitiveCommands_Success()
        {
            MockSecretsManager secretsManager = new MockSecretsManager();
            CommandExecutor executor = CreateCommandExecutor(secretsManager: secretsManager);

            int exitCode = await executor.ExecuteAsync(new[] { "SECRET-SET", "key", "value" });

            Assert.Equal(0, exitCode);
            Assert.Contains(secretsManager.MethodCalls, call => call.StartsWith("SetSecretAsync"));
        }

        private CommandExecutor CreateCommandExecutor(
            MockConfigManager? configManager = null,
            MockSecretsManager? secretsManager = null,
            MockServiceManager? serviceManager = null)
        {
            configManager ??= new MockConfigManager();
            secretsManager ??= new MockSecretsManager();
            serviceManager ??= new MockServiceManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            MockUnitFileManager unitFileManager = new MockUnitFileManager
            {
                TemplateWithSubstitutions = "[Unit]\nDescription=test\n",
            };

            NewCommand newCommand = new NewCommand(configManager, serviceManager, outputWriter, unitFileManager);
            UpdateCommand updateCommand = new UpdateCommand(
                configManager,
                secretsManager,
                serviceManager,
                new MockSymlinkManager(),
                new MockExecutableDetector(),
                new MockDistributionServiceClient(),
                outputWriter,
                new MockVersionExtractor(),
                new MockFilePermissionManager()
            );
            SetRemoteCommand setRemoteCommand = new SetRemoteCommand(configManager, outputWriter);
            SetExecNameCommand setExecNameCommand = new SetExecNameCommand(configManager, outputWriter);
            DistInstallCommand distInstallCommand = new DistInstallCommand(configManager, new HttpClient(), outputWriter);
            SecretSetCommand secretSetCommand = new SecretSetCommand(secretsManager, outputWriter);

            TimerCommand timerCommand = new TimerCommand(new MockTimerManager(), outputWriter);

            return new CommandExecutor(
                newCommand,
                updateCommand,
                setRemoteCommand,
                setExecNameCommand,
                distInstallCommand,
                secretSetCommand,
                timerCommand,
                outputWriter
            );
        }
    }
}

