using Updaemon.Commands;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class UpdateCommandTests
    {
        [Fact]
        public async Task ExecuteAsync_NoPluginConfigured_DoesNotProceed()
        {
            MockConfigManager configManager = new MockConfigManager();
            MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();

            UpdateCommand command = new UpdateCommand(
                configManager,
                new MockSecretsManager(),
                new MockServiceManager(),
                distributionClient,
                new MockOutputWriter(),
                new MockVersionExtractor(),
                new MockServiceDeployer()
            );

            int exitCode = await command.ExecuteAsync(Array.Empty<string>());
            Assert.Equal(0, exitCode);

            Assert.DoesNotContain(distributionClient.MethodCalls, call => call.StartsWith("ConnectAsync"));
        }

        [Fact]
        public async Task ExecuteAsync_SpecificServiceNotRegistered_DoesNotProceed()
        {
            MockConfigManager configManager = new MockConfigManager();
            InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" };
            await configManager.AddOrUpdatePluginAsync(pluginInfo);

            MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();

            UpdateCommand command = new UpdateCommand(
                configManager,
                new MockSecretsManager(),
                new MockServiceManager(),
                distributionClient,
                new MockOutputWriter(),
                new MockVersionExtractor(),
                new MockServiceDeployer()
            );

            int exitCode = await command.ExecuteAsync(new[] { "non-existent-service" });
            Assert.Equal(1, exitCode);

            Assert.DoesNotContain(distributionClient.MethodCalls, call => call.StartsWith("GetLatestVersionAsync"));
        }

        [Fact]
        public async Task ExecuteAsync_NoServicesRegistered_DoesNotProceed()
        {
            MockConfigManager configManager = new MockConfigManager();
            InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" };
            await configManager.AddOrUpdatePluginAsync(pluginInfo);

            MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();

            UpdateCommand command = new UpdateCommand(
                configManager,
                new MockSecretsManager(),
                new MockServiceManager(),
                distributionClient,
                new MockOutputWriter(),
                new MockVersionExtractor(),
                new MockServiceDeployer()
            );

            int exitCode = await command.ExecuteAsync(Array.Empty<string>());
            Assert.Equal(0, exitCode);

            Assert.DoesNotContain(distributionClient.MethodCalls, call => call.StartsWith("GetLatestVersionAsync"));
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesSpecificServiceWhenAppNameProvided()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockServiceDeployer serviceDeployer = new MockServiceDeployer();
                serviceDeployer.SetInitialized("my-api", "/opt/my-api/0.9.0");
                serviceDeployer.SetDeployResult("my-api", new Version(1, 0, 0));

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 0, 0));

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    new MockSecretsManager(),
                    new MockServiceManager(),
                    distributionClient,
                    new MockOutputWriter(),
                    new MockVersionExtractor(),
                    serviceDeployer
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                Assert.Contains(distributionClient.MethodCalls, call => call == "GetLatestVersionAsync:MyApi");
                Assert.Contains(serviceDeployer.MethodCalls, call => call == "DeployVersionAsync:my-api:1.0.0");
            }
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesAllServicesWhenNoAppNameProvided()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("service1", "Service1", "github");
                await configManager.RegisterServiceAsync("service2", "Service2", "github");

                MockServiceDeployer serviceDeployer = new MockServiceDeployer();
                serviceDeployer.SetInitialized("service1", "/opt/service1/0.9.0");
                serviceDeployer.SetInitialized("service2", "/opt/service2/0.9.0");
                serviceDeployer.SetDeployResult("service1", new Version(1, 0, 0));
                serviceDeployer.SetDeployResult("service2", new Version(1, 0, 0));

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("Service1", new Version(1, 0, 0));
                distributionClient.SetLatestVersion("Service2", new Version(1, 0, 0));

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    new MockSecretsManager(),
                    new MockServiceManager(),
                    distributionClient,
                    new MockOutputWriter(),
                    new MockVersionExtractor(),
                    serviceDeployer
                );

                int exitCode = await command.ExecuteAsync(Array.Empty<string>());
                Assert.Equal(0, exitCode);

                Assert.Contains(distributionClient.MethodCalls, call => call == "GetLatestVersionAsync:Service1");
                Assert.Contains(distributionClient.MethodCalls, call => call == "GetLatestVersionAsync:Service2");
            }
        }

        [Fact]
        public async Task UpdateService_AlreadyUpToDate_SkipsUpdate()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockServiceDeployer serviceDeployer = new MockServiceDeployer();
                serviceDeployer.SetInitialized("my-api", "/opt/my-api/1.0.0");

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 0, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                versionExtractor.ExtractVersionFromPathResult = new Version(1, 0, 0);

                MockServiceManager serviceManager = new MockServiceManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    new MockSecretsManager(),
                    serviceManager,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    serviceDeployer
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                // Should not deploy if already up to date
                Assert.DoesNotContain(serviceDeployer.MethodCalls, call => call.StartsWith("DeployVersionAsync"));

                // Should not call any service manager methods (no restart/start)
                Assert.Empty(serviceManager.MethodCalls.Where(call =>
                    call.Contains("Start") || call.Contains("Restart") || call.Contains("Stop")));
            }
        }

        [Fact]
        public async Task UpdateService_NewerVersionAvailable_DeploysNewVersion()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockServiceDeployer serviceDeployer = new MockServiceDeployer();
                serviceDeployer.SetInitialized("my-api", "/opt/my-api/1.0.0");
                serviceDeployer.SetDeployResult("my-api", new Version(1, 1, 0));

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 1, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                versionExtractor.ExtractVersionFromPathResult = new Version(1, 0, 0);

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    new MockSecretsManager(),
                    new MockServiceManager(),
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    serviceDeployer
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                Assert.Contains(serviceDeployer.MethodCalls, call => call == "DeployVersionAsync:my-api:1.1.0");
            }
        }

        [Fact]
        public async Task UpdateService_RestartsRunningService()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockServiceDeployer serviceDeployer = new MockServiceDeployer();
                serviceDeployer.SetInitialized("my-api", "/opt/my-api/1.0.0");
                serviceDeployer.SetDeployResult("my-api", new Version(1, 1, 0));

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 1, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                versionExtractor.ExtractVersionFromPathResult = new Version(1, 0, 0);

                MockServiceManager serviceManager = new MockServiceManager();
                serviceManager.ServiceExistsStates["my-api"] = true;
                serviceManager.ServiceRunningStates["my-api"] = true;

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    new MockSecretsManager(),
                    serviceManager,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    serviceDeployer
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                Assert.Contains(serviceManager.MethodCalls, call => call == "RestartServiceAsync:my-api");
            }
        }

        [Fact]
        public async Task UpdateService_StartsStoppedService()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockServiceDeployer serviceDeployer = new MockServiceDeployer();
                serviceDeployer.SetInitialized("my-api", "/opt/my-api/1.0.0");
                serviceDeployer.SetDeployResult("my-api", new Version(1, 1, 0));

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 1, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                versionExtractor.ExtractVersionFromPathResult = new Version(1, 0, 0);

                MockServiceManager serviceManager = new MockServiceManager();
                serviceManager.ServiceExistsStates["my-api"] = true;
                serviceManager.ServiceRunningStates["my-api"] = false;

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    new MockSecretsManager(),
                    serviceManager,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    serviceDeployer
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                Assert.Contains(serviceManager.MethodCalls, call => call == "StartServiceAsync:my-api");
                Assert.DoesNotContain(serviceManager.MethodCalls, call => call == "RestartServiceAsync:my-api");
            }
        }

        [Fact]
        public async Task UpdateService_DeployFailed_DoesNotRestartService()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockServiceDeployer serviceDeployer = new MockServiceDeployer();
                serviceDeployer.SetInitialized("my-api", "/opt/my-api/0.9.0");
                // Don't configure deploy result — deployer returns null (executable not found)

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 0, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                versionExtractor.ExtractVersionFromPathResult = new Version(0, 9, 0);

                MockServiceManager serviceManager = new MockServiceManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    new MockSecretsManager(),
                    serviceManager,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    serviceDeployer
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                // Deploy was attempted but returned null
                Assert.Contains(serviceDeployer.MethodCalls, call => call == "DeployVersionAsync:my-api:1.0.0");
                // Should not restart/start service
                Assert.Empty(serviceManager.MethodCalls.Where(call =>
                    call.Contains("Start") || call.Contains("Restart")));
            }
        }

        [Fact]
        public async Task UpdateService_InitializesDistributionClientWithSecrets()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockSecretsManager secretsManager = new MockSecretsManager();
                await secretsManager.SetSecretAsync("github", "apiKey", "abc123");
                await secretsManager.SetSecretAsync("github", "tenantId", "550e8400");

                MockServiceDeployer serviceDeployer = new MockServiceDeployer();
                serviceDeployer.SetInitialized("my-api", "/opt/my-api/0.9.0");
                serviceDeployer.SetDeployResult("my-api", new Version(1, 0, 0));

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 0, 0));

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    new MockServiceManager(),
                    distributionClient,
                    new MockOutputWriter(),
                    new MockVersionExtractor(),
                    serviceDeployer
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                Assert.NotNull(distributionClient.InitializedSecrets);
                Assert.Contains("apiKey=abc123", distributionClient.InitializedSecrets);
                Assert.Contains("tenantId=550e8400", distributionClient.InitializedSecrets);
            }
        }

        [Fact]
        public async Task ExecuteAsync_MultiplePlugins_GroupsByPluginAndInitializesWithPerPluginSecrets()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string githubPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                string byteshelfPath = tempHelper.CreateTempFile("plugins/byteshelf/bin", "fake-plugin");
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = githubPath });
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "byteshelf", Path = byteshelfPath });
                await configManager.RegisterServiceAsync("svc1", "Svc1", "github");
                await configManager.RegisterServiceAsync("svc2", "Svc2", "byteshelf");

                MockSecretsManager secretsManager = new MockSecretsManager();
                await secretsManager.SetSecretAsync("github", "token", "gh123");
                await secretsManager.SetSecretAsync("byteshelf", "apiKey", "bs456");

                MockServiceDeployer serviceDeployer = new MockServiceDeployer();
                serviceDeployer.SetInitialized("svc1", "/opt/svc1/0.9.0");
                serviceDeployer.SetInitialized("svc2", "/opt/svc2/0.9.0");
                serviceDeployer.SetDeployResult("svc1", new Version(1, 0, 0));
                serviceDeployer.SetDeployResult("svc2", new Version(1, 0, 0));

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("Svc1", new Version(1, 0, 0));
                distributionClient.SetLatestVersion("Svc2", new Version(1, 0, 0));

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    new MockServiceManager(),
                    distributionClient,
                    new MockOutputWriter(),
                    new MockVersionExtractor(),
                    serviceDeployer
                );

                int exitCode = await command.ExecuteAsync(Array.Empty<string>());
                Assert.Equal(0, exitCode);

                // One connect per plugin
                Assert.Contains(distributionClient.MethodCalls, c => c.StartsWith("ConnectAsync:") && c.Contains("github"));
                Assert.Contains(distributionClient.MethodCalls, c => c.StartsWith("ConnectAsync:") && c.Contains("byteshelf"));

                // Initialize with per-plugin secrets
                Assert.Contains(distributionClient.MethodCalls, c => c.StartsWith("InitializeAsync:token=gh123"));
                Assert.Contains(distributionClient.MethodCalls, c => c.StartsWith("InitializeAsync:apiKey=bs456"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_ServiceWithoutPluginAlias_SkipsWithError()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = pluginPath });
                await configManager.RegisterServiceAsync("old-service", "OldService", "github");
                UpdaemonConfig config = await configManager.LoadConfigAsync();
                RegisteredService serviceWithoutAlias = config.Services.First();
                serviceWithoutAlias.DistributionPluginAlias = "";
                await configManager.SaveConfigAsync(config);

                MockOutputWriter outputWriter = new MockOutputWriter();
                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    new MockSecretsManager(),
                    new MockServiceManager(),
                    distributionClient,
                    outputWriter,
                    new MockVersionExtractor(),
                    new MockServiceDeployer()
                );

                int exitCode = await command.ExecuteAsync(Array.Empty<string>());
                Assert.Equal(1, exitCode);

                Assert.Contains(outputWriter.Errors, e => e.Contains("does not have a distribution plugin assigned"));
                Assert.DoesNotContain(distributionClient.MethodCalls, c => c.StartsWith("ConnectAsync"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginNotFound_SkipsWithError()
        {
            MockConfigManager configManager = new MockConfigManager();
            await configManager.RegisterServiceAsync("my-service", "MyService", "non-existent-plugin");

            MockOutputWriter outputWriter = new MockOutputWriter();
            MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();

            UpdateCommand command = new UpdateCommand(
                configManager,
                new MockSecretsManager(),
                new MockServiceManager(),
                distributionClient,
                outputWriter,
                new MockVersionExtractor(),
                new MockServiceDeployer()
            );

            int exitCode = await command.ExecuteAsync(Array.Empty<string>());
            Assert.Equal(0, exitCode);

            Assert.Contains(outputWriter.Errors, e => e.Contains("Plugin 'non-existent-plugin' not found"));
            Assert.DoesNotContain(distributionClient.MethodCalls, c => c.StartsWith("ConnectAsync"));
        }

        [Fact]
        public async Task ExecuteAsync_UninitializedService_SkipsWithWarning()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = pluginPath });
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockOutputWriter outputWriter = new MockOutputWriter();
                MockServiceDeployer serviceDeployer = new MockServiceDeployer();
                // No initialized target — service is not initialized

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 0, 0));

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    new MockSecretsManager(),
                    new MockServiceManager(),
                    distributionClient,
                    outputWriter,
                    new MockVersionExtractor(),
                    serviceDeployer
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                Assert.Contains(outputWriter.Messages, m => m.Contains("not initialized") && m.Contains("updaemon init"));
                Assert.DoesNotContain(serviceDeployer.MethodCalls, c => c.StartsWith("DeployVersionAsync"));
            }
        }

        [Fact]
        public async Task UpdateService_CliType_DoesNotRestartService()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-tool", "MyTool", "github", ServiceType.Cli);

                MockServiceDeployer serviceDeployer = new MockServiceDeployer();
                serviceDeployer.SetInitialized("my-tool", "/opt/my-tool/1.0.0");
                serviceDeployer.SetDeployResult("my-tool", new Version(1, 1, 0));

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyTool", new Version(1, 1, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                versionExtractor.ExtractVersionFromPathResult = new Version(1, 0, 0);

                MockServiceManager serviceManager = new MockServiceManager();
                MockOutputWriter outputWriter = new MockOutputWriter();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    new MockSecretsManager(),
                    serviceManager,
                    distributionClient,
                    outputWriter,
                    versionExtractor,
                    serviceDeployer
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-tool" });
                Assert.Equal(0, exitCode);

                // Should have deployed the new version
                Assert.Contains(serviceDeployer.MethodCalls, call => call == "DeployVersionAsync:my-tool:1.1.0");

                // Should NOT have called any service manager methods
                Assert.Empty(serviceManager.MethodCalls.Where(call =>
                    call.Contains("Start") || call.Contains("Restart") || call.Contains("ServiceExists") || call.Contains("IsServiceRunning")));

                // Should print CLI success message
                Assert.Contains(outputWriter.Messages, m => m.Contains("CLI tool updated successfully"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginExecutableNotFound_SkipsWithError()
        {
            MockConfigManager configManager = new MockConfigManager();
            await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = "/nonexistent/plugin/path" });
            await configManager.RegisterServiceAsync("my-service", "MyService", "github");

            MockOutputWriter outputWriter = new MockOutputWriter();
            MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();

            UpdateCommand command = new UpdateCommand(
                configManager,
                new MockSecretsManager(),
                new MockServiceManager(),
                distributionClient,
                outputWriter,
                new MockVersionExtractor(),
                new MockServiceDeployer()
            );

            int exitCode = await command.ExecuteAsync(Array.Empty<string>());
            Assert.Equal(0, exitCode);

            Assert.Contains(outputWriter.Errors, e => e.Contains("Plugin executable not found"));
            Assert.DoesNotContain(distributionClient.MethodCalls, c => c.StartsWith("ConnectAsync"));
        }
    }
}
