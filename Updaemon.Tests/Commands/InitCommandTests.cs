using Updaemon.Commands;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class InitCommandTests
    {
        private class InitCommandTestBuilder : IDisposable
        {
            public MockConfigManager ConfigManager { get; } = new MockConfigManager();
            public MockSecretsManager SecretsManager { get; } = new MockSecretsManager();
            public MockServiceManager ServiceManager { get; } = new MockServiceManager();
            public MockDistributionServiceClient DistributionClient { get; } = new MockDistributionServiceClient();
            public MockOutputWriter OutputWriter { get; } = new MockOutputWriter();
            public MockUnitFileManager UnitFileManager { get; } = new MockUnitFileManager();
            public MockServiceDeployer ServiceDeployer { get; } = new MockServiceDeployer();
            public TempFileHelper TempHelper { get; } = new TempFileHelper();
            public string SystemdDirectory { get; }

            public InitCommandTestBuilder()
            {
                SystemdDirectory = TempHelper.CreateTempDirectory("systemd");
                ServiceDeployer.ServiceBaseDirectory = TempHelper.TempDirectory;
            }

            public InitCommand Build()
            {
                return new InitCommand(
                    ConfigManager, SecretsManager, ServiceManager,
                    DistributionClient, OutputWriter, UnitFileManager,
                    ServiceDeployer, SystemdDirectory);
            }

            /// <summary>
            /// Registers a service with a valid plugin so the command passes validation.
            /// </summary>
            public async Task RegisterServiceWithPluginAsync(
                string localName = "my-api",
                string remoteName = "owner/repo",
                string pluginAlias = "github")
            {
                string pluginPath = TempHelper.CreateTempFile("plugin/github-dist", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = pluginAlias, Path = pluginPath };
                await ConfigManager.AddOrUpdatePluginAsync(pluginInfo);
                await ConfigManager.RegisterServiceAsync(localName, remoteName, pluginAlias);
            }

            public void Dispose()
            {
                TempHelper.Dispose();
            }
        }

        [Fact]
        public async Task ExecuteAsync_HappyPath_DownloadsAndSetsUpService()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.RegisterServiceWithPluginAsync();

                b.DistributionClient.SetLatestVersion("owner/repo", new Version(1, 0, 0));
                b.UnitFileManager.TemplateWithSubstitutions = "[Unit]\nDescription=test\n";
                DeployResult deployResult = b.ServiceDeployer.SetDeployResult("my-api", new Version(1, 0, 0), "MyApi.Server");

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api" });

                Assert.Equal(0, exitCode);
                Assert.Contains(b.ServiceDeployer.MethodCalls, c => c == "DeployVersionAsync:my-api:1.0.0");
                Assert.True(File.Exists(Path.Combine(b.SystemdDirectory, "my-api.service")));
                Assert.Contains(b.ServiceManager.MethodCalls, c => c == "DaemonReloadAsync");
                Assert.Contains(b.ServiceManager.MethodCalls, c => c == "EnableServiceAsync:my-api");
                Assert.Contains(b.ServiceManager.MethodCalls, c => c == "StartServiceAsync:my-api");
                Assert.True(b.DistributionClient.IsDisposed);
            }
        }

        [Fact]
        public async Task ExecuteAsync_AlreadyInitialized_ReturnsSuccessWithMessage()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.RegisterServiceWithPluginAsync();
                b.ServiceDeployer.SetInitialized("my-api", "/opt/my-api/1.0.0");

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api" });

                Assert.Equal(0, exitCode);
                Assert.Contains(b.OutputWriter.Messages, m => m.Contains("already initialized"));
                Assert.Empty(b.DistributionClient.Downloads);
            }
        }

        [Fact]
        public async Task ExecuteAsync_ServiceNotRegistered_ReturnsError()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api" });

                Assert.Equal(1, exitCode);
                Assert.Contains(b.OutputWriter.Errors, e => e.Contains("not registered"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginNotFound_ReturnsError()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.ConfigManager.RegisterServiceAsync("my-api", "owner/repo", "github");

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api" });

                Assert.Equal(1, exitCode);
                Assert.Contains(b.OutputWriter.Errors, e => e.Contains("Plugin") && e.Contains("not found"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginExecutableMissing_ReturnsError()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/nonexistent/plugin" };
                await b.ConfigManager.AddOrUpdatePluginAsync(pluginInfo);
                await b.ConfigManager.RegisterServiceAsync("my-api", "owner/repo", "github");

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api" });

                Assert.Equal(1, exitCode);
                Assert.Contains(b.OutputWriter.Errors, e => e.Contains("Plugin executable not found"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_NoVersionAvailable_ReturnsError()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.RegisterServiceWithPluginAsync();
                // Don't set any version — plugin returns null

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api" });

                Assert.Equal(1, exitCode);
                Assert.Contains(b.OutputWriter.Errors, e => e.Contains("No version available"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_ExecutableNotFound_ReturnsError()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.RegisterServiceWithPluginAsync();
                b.DistributionClient.SetLatestVersion("owner/repo", new Version(1, 0, 0));
                // Don't configure deploy result — deployer returns null

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api" });

                Assert.Equal(1, exitCode);
            }
        }

        [Fact]
        public async Task ExecuteAsync_WithoutAppName_ReturnsError()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(Array.Empty<string>());

                Assert.Equal(1, exitCode);
                Assert.Contains(b.OutputWriter.Errors, e => e.Contains("Missing required argument"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_UsesDetectedExecutableNameInUnitFile()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.RegisterServiceWithPluginAsync();
                b.DistributionClient.SetLatestVersion("owner/repo", new Version(1, 0, 0));
                b.ServiceDeployer.SetDeployResult("my-api", new Version(1, 0, 0), "MyApi.Server");

                string? capturedExecName = null;
                CapturingUnitFileManager capturingManager = new CapturingUnitFileManager(
                    (serviceName, symlinkPath, executableName) => capturedExecName = executableName);

                // Build manually with the capturing unit file manager
                InitCommand command = new InitCommand(
                    b.ConfigManager, b.SecretsManager, b.ServiceManager,
                    b.DistributionClient, b.OutputWriter, capturingManager,
                    b.ServiceDeployer, b.SystemdDirectory);

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });

                Assert.Equal(0, exitCode);
                Assert.Equal("MyApi.Server", capturedExecName);
            }
        }

        [Fact]
        public async Task ExecuteAsync_FailureAfterDeploy_CleansUpArtifacts()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.RegisterServiceWithPluginAsync();
                b.DistributionClient.SetLatestVersion("owner/repo", new Version(1, 0, 0));
                b.ServiceDeployer.SetDeployResult("my-api", new Version(1, 0, 0));
                b.UnitFileManager.TemplateWithSubstitutions = "[Unit]\nDescription=test\n";

                // Make EnableServiceAsync throw to simulate post-deploy failure
                b.ServiceManager.ThrowOnEnable = true;

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-api" });

                Assert.Equal(1, exitCode);
                // Should have called cleanup on the deployer
                Assert.Contains(b.ServiceDeployer.MethodCalls, c => c.StartsWith("CleanupDeployAsync:"));
                Assert.Single(b.ServiceDeployer.CleanedUpDeploys);
            }
        }

        /// <summary>
        /// A unit file manager that captures the arguments passed to ReadTemplateWithSubstitutionsAsync.
        /// </summary>
        private class CapturingUnitFileManager : IUnitFileManager
        {
            private readonly Action<string, string, string> _onSubstitution;

            public CapturingUnitFileManager(Action<string, string, string> onSubstitution)
            {
                _onSubstitution = onSubstitution;
            }

            public Task<string> ReadTemplateAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(string.Empty);
            }

            public Task<string> ReadTemplateWithSubstitutionsAsync(string serviceName, string symlinkPath, string executableName, CancellationToken cancellationToken = default)
            {
                _onSubstitution(serviceName, symlinkPath, executableName);
                return Task.FromResult("[Unit]\nDescription=test\n");
            }

            public async Task WriteUnitFileAsync(string unitFilePath, string serviceName, string symlinkPath, string executableName, CancellationToken cancellationToken = default)
            {
                string content = await ReadTemplateWithSubstitutionsAsync(serviceName, symlinkPath, executableName, cancellationToken);
                await File.WriteAllTextAsync(unitFilePath, content, cancellationToken);
            }
        }
    }
}
