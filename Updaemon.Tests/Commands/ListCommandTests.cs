using Updaemon.Commands;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class ListCommandTests
    {
        private readonly MockConfigManager _configManager = new MockConfigManager();
        private readonly MockOutputWriter _output = new MockOutputWriter();
        private readonly MockServiceDeployer _serviceDeployer = new MockServiceDeployer();
        private readonly TestVersionExtractor _versionExtractor = new TestVersionExtractor();

        private ListCommand CreateCommand()
        {
            return new ListCommand(_configManager, _output, _serviceDeployer, _versionExtractor);
        }

        [Fact]
        public async Task ExecuteAsync_WhenNoServicesRegistered_ShowsEmptyMessage()
        {
            ListCommand command = CreateCommand();
            int exitCode = await command.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(0, exitCode);
            Assert.Contains(_output.Messages, line => line.Contains("No applications registered."));
            Assert.Contains(_output.Messages, line => line.Contains("updaemon new <app-name> --from"));
        }

        [Fact]
        public async Task ExecuteAsync_WithSingleInitializedService_ShowsVersionAndDetails()
        {
            await _configManager.RegisterServiceAsync("my-api", "my-org/my-api", "github", ServiceType.Service);
            _serviceDeployer.SetInitialized("my-api", "/opt/my-api/2.1.0");

            ListCommand command = CreateCommand();
            int exitCode = await command.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(0, exitCode);
            Assert.Contains(_output.Messages, line => line.Contains("Registered applications (1):"));
            Assert.Contains(_output.Messages, line => line.Contains("Name: my-api"));
            Assert.Contains(_output.Messages, line => line.Contains("Type: service"));
            Assert.Contains(_output.Messages, line => line.Contains("Version: 2.1.0"));
            Assert.Contains(_output.Messages, line => line.Contains("Distribution plugin: github"));
            Assert.Contains(_output.Messages, line => line.Contains("Remote name: my-org/my-api"));
        }

        [Fact]
        public async Task ExecuteAsync_WithUninitializedService_ShowsNotInitialized()
        {
            await _configManager.RegisterServiceAsync("my-api", "my-org/my-api", "github");

            ListCommand command = CreateCommand();
            int exitCode = await command.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(0, exitCode);
            Assert.Contains(_output.Messages, line => line.Contains("Version: not initialized"));
        }

        [Fact]
        public async Task ExecuteAsync_WithCliTool_ShowsCliToolType()
        {
            await _configManager.RegisterServiceAsync("my-tool", "my-org/my-tool", "github", ServiceType.Cli);
            _serviceDeployer.SetInitialized("my-tool", "/opt/my-tool/1.0.0");

            ListCommand command = CreateCommand();
            int exitCode = await command.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(0, exitCode);
            Assert.Contains(_output.Messages, line => line.Contains("Type: CLI tool"));
        }

        [Fact]
        public async Task ExecuteAsync_ServiceWithDefaultRestart_ShowsRestartAuto()
        {
            await _configManager.RegisterServiceAsync("my-api", "my-org/my-api", "github", ServiceType.Service);
            _serviceDeployer.SetInitialized("my-api", "/opt/my-api/1.0.0");

            ListCommand command = CreateCommand();
            int exitCode = await command.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(0, exitCode);
            Assert.Contains(_output.Messages, line => line.Contains("Restart: auto"));
        }

        [Fact]
        public async Task ExecuteAsync_ServiceWithManualRestart_ShowsRestartManual()
        {
            await _configManager.RegisterServiceAsync("my-api", "my-org/my-api", "github", ServiceType.Service);
            await _configManager.SetAutoRestartAsync("my-api", false);
            _serviceDeployer.SetInitialized("my-api", "/opt/my-api/1.0.0");

            ListCommand command = CreateCommand();
            int exitCode = await command.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(0, exitCode);
            Assert.Contains(_output.Messages, line => line.Contains("Restart: manual"));
        }

        [Fact]
        public async Task ExecuteAsync_CliTool_DoesNotShowRestart()
        {
            await _configManager.RegisterServiceAsync("my-tool", "my-org/my-tool", "github", ServiceType.Cli);
            _serviceDeployer.SetInitialized("my-tool", "/opt/my-tool/1.0.0");

            ListCommand command = CreateCommand();
            int exitCode = await command.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain(_output.Messages, line => line.Contains("Restart:"));
        }

        [Fact]
        public async Task ExecuteAsync_WithMixedInitializedAndUninitialized_ShowsBoth()
        {
            await _configManager.RegisterServiceAsync("app-a", "org/app-a", "github");
            await _configManager.RegisterServiceAsync("app-b", "org/app-b", "github");
            _serviceDeployer.SetInitialized("app-a", "/opt/app-a/1.0.0");

            ListCommand command = CreateCommand();
            int exitCode = await command.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(0, exitCode);
            Assert.Contains(_output.Messages, line => line.Contains("Registered applications (2):"));
            Assert.Contains(_output.Messages, line => line.Contains("Name: app-a"));
            Assert.Contains(_output.Messages, line => line.Contains("Name: app-b"));
            Assert.Contains(_output.Messages, line => line.Contains("Version: 1.0.0"));
            Assert.Contains(_output.Messages, line => line.Contains("Version: not initialized"));
        }

        [Fact]
        public async Task ExecuteAsync_WhenVersionCannotBeExtracted_ShowsUnknown()
        {
            await _configManager.RegisterServiceAsync("my-api", "my-org/my-api", "github");
            _serviceDeployer.SetInitialized("my-api", "/opt/my-api/current");

            ListCommand command = CreateCommand();
            int exitCode = await command.ExecuteAsync(Array.Empty<string>());

            Assert.Equal(0, exitCode);
            Assert.Contains(_output.Messages, line => line.Contains("Version: unknown"));
        }
    }
}
