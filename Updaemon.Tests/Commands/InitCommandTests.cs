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
            public MockSymlinkManager SymlinkManager { get; } = new MockSymlinkManager();
            public TempFileHelper TempHelper { get; } = new TempFileHelper();
            public string SystemdDirectory { get; }
            public string BinDirectory { get; }

            public InitCommandTestBuilder()
            {
                SystemdDirectory = TempHelper.CreateTempDirectory("systemd");
                BinDirectory = TempHelper.CreateTempDirectory("bin");
                ServiceDeployer.ServiceBaseDirectory = TempHelper.TempDirectory;
                // Make the mock unit-file manager produce paths inside the test's systemd dir
                // so InitCommand's GetUnitFilePath call writes to a writable location.
                UnitFileManager.UnitFileDirectory = SystemdDirectory;
            }

            public InitCommand Build()
            {
                return new InitCommand(
                    ConfigManager, SecretsManager, ServiceManager,
                    DistributionClient, OutputWriter, UnitFileManager,
                    ServiceDeployer, SymlinkManager, BinDirectory);
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

            /// <summary>
            /// Registers a CLI tool with a valid plugin so the command passes validation.
            /// </summary>
            public async Task RegisterCliToolWithPluginAsync(
                string localName = "my-tool",
                string remoteName = "owner/tool",
                string pluginAlias = "github")
            {
                string pluginPath = TempHelper.CreateTempFile("plugin/github-dist", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = pluginAlias, Path = pluginPath };
                await ConfigManager.AddOrUpdatePluginAsync(pluginInfo);
                await ConfigManager.RegisterServiceAsync(localName, remoteName, pluginAlias, ServiceType.Cli);
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
                    (serviceName, symlinkPath, executableName) => capturedExecName = executableName)
                {
                    UnitFileDirectory = b.SystemdDirectory,
                };

                // Build manually with the capturing unit file manager
                InitCommand command = new InitCommand(
                    b.ConfigManager, b.SecretsManager, b.ServiceManager,
                    b.DistributionClient, b.OutputWriter, capturingManager,
                    b.ServiceDeployer, b.SymlinkManager, b.BinDirectory);

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

        [Fact]
        public async Task ExecuteAsync_CliTool_HappyPath_CreatesBothSymlinks()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.RegisterCliToolWithPluginAsync(localName: "rg", remoteName: "BurntSushi/ripgrep");

                b.DistributionClient.SetLatestVersion("BurntSushi/ripgrep", new Version(1, 0, 0));
                b.ServiceDeployer.SetDeployResult("rg", new Version(1, 0, 0), "ripgrep");

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "rg" });

                Assert.Equal(0, exitCode);
                Assert.Contains(b.ServiceDeployer.MethodCalls, c => c == "DeployVersionAsync:rg:1.0.0");

                // Should have created a bin symlink with the executable name
                string expectedBinPath = Path.Combine(b.BinDirectory, "ripgrep");
                Assert.Contains(b.SymlinkManager.Symlinks, kv => kv.Key == expectedBinPath);

                // Should also have created an alias symlink with the local name
                string expectedAliasPath = Path.Combine(b.BinDirectory, "rg");
                Assert.Contains(b.SymlinkManager.Symlinks, kv => kv.Key == expectedAliasPath);

                Assert.Contains(b.OutputWriter.Messages, m => m.Contains("CLI tool") && m.Contains("initialized successfully"));
                Assert.True(b.DistributionClient.IsDisposed);
            }
        }

        [Fact]
        public async Task ExecuteAsync_CliTool_SameLocalAndExecutableName_CreatesSingleSymlink()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.RegisterCliToolWithPluginAsync();

                b.DistributionClient.SetLatestVersion("owner/tool", new Version(1, 0, 0));
                b.ServiceDeployer.SetDeployResult("my-tool", new Version(1, 0, 0), "my-tool");

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-tool" });

                Assert.Equal(0, exitCode);

                // Only one symlink should exist since local name matches executable name
                string expectedBinPath = Path.Combine(b.BinDirectory, "my-tool");
                Assert.Single(b.SymlinkManager.Symlinks);
                Assert.Contains(b.SymlinkManager.Symlinks, kv => kv.Key == expectedBinPath);
            }
        }

        [Fact]
        public async Task ExecuteAsync_CliTool_SkipsSystemdSetup()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.RegisterCliToolWithPluginAsync(localName: "my-tool", remoteName: "owner/tool");

                b.DistributionClient.SetLatestVersion("owner/tool", new Version(1, 0, 0));
                b.ServiceDeployer.SetDeployResult("my-tool", new Version(1, 0, 0), "my-tool");

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-tool" });

                Assert.Equal(0, exitCode);

                // No systemd calls should have been made
                Assert.DoesNotContain(b.ServiceManager.MethodCalls, c => c == "DaemonReloadAsync");
                Assert.DoesNotContain(b.ServiceManager.MethodCalls, c => c.StartsWith("EnableServiceAsync"));
                Assert.DoesNotContain(b.ServiceManager.MethodCalls, c => c.StartsWith("StartServiceAsync"));

                // No unit file should exist
                Assert.False(File.Exists(Path.Combine(b.SystemdDirectory, "my-tool.service")));
            }
        }

        [Fact]
        public async Task ExecuteAsync_CliTool_AlreadyInitialized_ReturnsSuccessWithMessage()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.RegisterCliToolWithPluginAsync();
                b.ServiceDeployer.SetInitialized("my-tool", "/opt/my-tool/1.0.0");

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-tool" });

                Assert.Equal(0, exitCode);
                Assert.Contains(b.OutputWriter.Messages, m => m.Contains("CLI tool") && m.Contains("already initialized"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_CliTool_FailureAfterDeploy_CleansUpSymlinksAndArtifacts()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.RegisterCliToolWithPluginAsync(localName: "rg", remoteName: "BurntSushi/ripgrep");

                b.DistributionClient.SetLatestVersion("BurntSushi/ripgrep", new Version(1, 0, 0));
                b.ServiceDeployer.SetDeployResult("rg", new Version(1, 0, 0), "ripgrep");

                // First symlink (bin) succeeds, second (alias) throws
                b.SymlinkManager.ThrowAfterCreateCount = 1;

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "rg" });

                Assert.Equal(1, exitCode);
                Assert.Contains(b.OutputWriter.Errors, e => e.Contains("Error during initialization"));

                // Should have called cleanup on the deployer
                Assert.Contains(b.ServiceDeployer.MethodCalls, c => c.StartsWith("CleanupDeployAsync:"));
                Assert.Single(b.ServiceDeployer.CleanedUpDeploys);
            }
        }

        [Fact]
        public async Task ExecuteAsync_CliTool_FailureOnBinSymlink_CleansUpArtifacts()
        {
            using (InitCommandTestBuilder b = new InitCommandTestBuilder())
            {
                await b.RegisterCliToolWithPluginAsync(localName: "my-tool", remoteName: "owner/tool");

                b.DistributionClient.SetLatestVersion("owner/tool", new Version(1, 0, 0));
                b.ServiceDeployer.SetDeployResult("my-tool", new Version(1, 0, 0), "my-tool");

                // Fail on the first symlink creation (the bin symlink)
                b.SymlinkManager.ThrowAfterCreateCount = 0;

                InitCommand command = b.Build();
                int exitCode = await command.ExecuteAsync(new[] { "my-tool" });

                Assert.Equal(1, exitCode);
                Assert.Contains(b.OutputWriter.Errors, e => e.Contains("Error during initialization"));

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

            public string UnitFileDirectory { get; set; } = "/etc/systemd/system";

            public string GetUnitFilePath(string serviceName) =>
                Path.Combine(UnitFileDirectory, serviceName + ".service");

            public Task EnsureWritableAsync(CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }
    }
}
