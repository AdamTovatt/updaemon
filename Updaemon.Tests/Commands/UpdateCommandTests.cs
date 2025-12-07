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
            MockSecretsManager secretsManager = new MockSecretsManager();
            MockServiceManager serviceManager = new MockServiceManager();
            MockSymlinkManager symlinkManager = new MockSymlinkManager();
            MockExecutableDetector executableDetector = new MockExecutableDetector();
            MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
            MockVersionExtractor versionExtractor = new MockVersionExtractor();
            MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

            UpdateCommand command = new UpdateCommand(
                configManager,
                secretsManager,
                serviceManager,
                symlinkManager,
                executableDetector,
                distributionClient,
                new MockOutputWriter(),
                versionExtractor,
                filePermissionManager
            );

            int exitCode = await command.ExecuteAsync(Array.Empty<string>());
            Assert.Equal(0, exitCode);

            // Should not connect to distribution client when no plugin configured
            Assert.DoesNotContain(distributionClient.MethodCalls, call => call.StartsWith("ConnectAsync"));
        }

        [Fact]
        public async Task ExecuteAsync_SpecificServiceNotRegistered_DoesNotProceed()
        {
            MockConfigManager configManager = new MockConfigManager();
            InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" };
            await configManager.AddOrUpdatePluginAsync(pluginInfo);

            MockSecretsManager secretsManager = new MockSecretsManager();
            MockServiceManager serviceManager = new MockServiceManager();
            MockSymlinkManager symlinkManager = new MockSymlinkManager();
            MockExecutableDetector executableDetector = new MockExecutableDetector();
            MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
            MockVersionExtractor versionExtractor = new MockVersionExtractor();
            MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

            UpdateCommand command = new UpdateCommand(
                configManager,
                secretsManager,
                serviceManager,
                symlinkManager,
                executableDetector,
                distributionClient,
                new MockOutputWriter(),
                versionExtractor,
                filePermissionManager
            );

            int exitCode = await command.ExecuteAsync(new[] { "non-existent-service" });
            Assert.Equal(1, exitCode);

            // Should not try to get latest version if service not registered
            Assert.DoesNotContain(distributionClient.MethodCalls, call => call.StartsWith("GetLatestVersionAsync"));
        }

        [Fact]
        public async Task ExecuteAsync_NoServicesRegistered_DoesNotProceed()
        {
            MockConfigManager configManager = new MockConfigManager();
            InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = "/path/to/plugin" };
            await configManager.AddOrUpdatePluginAsync(pluginInfo);

            MockSecretsManager secretsManager = new MockSecretsManager();
            MockServiceManager serviceManager = new MockServiceManager();
            MockSymlinkManager symlinkManager = new MockSymlinkManager();
            MockExecutableDetector executableDetector = new MockExecutableDetector();
            MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
            MockVersionExtractor versionExtractor = new MockVersionExtractor();
            MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

            UpdateCommand command = new UpdateCommand(
                configManager,
                secretsManager,
                serviceManager,
                symlinkManager,
                executableDetector,
                distributionClient,
                new MockOutputWriter(),
                versionExtractor,
                filePermissionManager
            );

            int exitCode = await command.ExecuteAsync(Array.Empty<string>());
            Assert.Equal(0, exitCode);

            // Should not try to get latest version if no services registered
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

                MockSecretsManager secretsManager = new MockSecretsManager();
                MockServiceManager serviceManager = new MockServiceManager();
                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                MockExecutableDetector executableDetector = new MockExecutableDetector();
                executableDetector.SetExecutableResult("/opt/my-api/1.0.0", "my-api", "/opt/my-api/1.0.0/my-api");

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 0, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    serviceManager,
                    symlinkManager,
                    executableDetector,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    filePermissionManager
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                Assert.Contains(distributionClient.MethodCalls, call => call == "GetLatestVersionAsync:MyApi");
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

                MockSecretsManager secretsManager = new MockSecretsManager();
                MockServiceManager serviceManager = new MockServiceManager();
                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                MockExecutableDetector executableDetector = new MockExecutableDetector();
                executableDetector.SetExecutableResult("/opt/service1/1.0.0", "service1", "/opt/service1/1.0.0/service1");
                executableDetector.SetExecutableResult("/opt/service2/1.0.0", "service2", "/opt/service2/1.0.0/service2");

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("Service1", new Version(1, 0, 0));
                distributionClient.SetLatestVersion("Service2", new Version(1, 0, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    serviceManager,
                    symlinkManager,
                    executableDetector,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    filePermissionManager
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
                string serviceBaseDirectory = "/opt";

                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockSecretsManager secretsManager = new MockSecretsManager();
                MockServiceManager serviceManager = new MockServiceManager();
                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                string currentSymlink = Path.Combine(serviceBaseDirectory, "my-api", "current");
                string currentExecutable = Path.Combine(serviceBaseDirectory, "my-api", "1.0.0", "my-api");
                symlinkManager.Symlinks[currentSymlink] = currentExecutable;

                MockExecutableDetector executableDetector = new MockExecutableDetector();
                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 0, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                versionExtractor.ExtractVersionFromPathResult = new Version(1, 0, 0);

                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    serviceManager,
                    symlinkManager,
                    executableDetector,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    filePermissionManager,
                    serviceBaseDirectory
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                // Should not download if already up to date
                Assert.Empty(distributionClient.Downloads);

                // Should not call any service manager methods (no restart/start)
                Assert.Empty(serviceManager.MethodCalls.Where(call =>
                    call.Contains("Start") || call.Contains("Restart") || call.Contains("Stop")));
            }
        }

        [Fact]
        public async Task UpdateService_NewerVersionAvailable_DownloadsAndInstalls()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceBaseDirectory = "/opt";

                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockSecretsManager secretsManager = new MockSecretsManager();
                MockServiceManager serviceManager = new MockServiceManager();
                serviceManager.ServiceExistsStates["my-api"] = true;
                serviceManager.ServiceRunningStates["my-api"] = true;

                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                string currentSymlink = Path.Combine(serviceBaseDirectory, "my-api", "current");
                string oldExecutable = Path.Combine(serviceBaseDirectory, "my-api", "1.0.0", "my-api");
                symlinkManager.Symlinks[currentSymlink] = oldExecutable;

                MockExecutableDetector executableDetector = new MockExecutableDetector();
                string newVersionDirectory = Path.Combine(serviceBaseDirectory, "my-api", "1.1.0");
                string newExecutable = Path.Combine(newVersionDirectory, "my-api");
                executableDetector.SetExecutableResult(newVersionDirectory, "my-api", newExecutable);

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 1, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                versionExtractor.ExtractVersionFromPathResult = new Version(1, 0, 0);

                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    serviceManager,
                    symlinkManager,
                    executableDetector,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    filePermissionManager,
                    serviceBaseDirectory
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                // Should download new version
                Assert.Single(distributionClient.Downloads);
                Assert.Equal("MyApi", distributionClient.Downloads[0].ServiceName);
                Assert.Equal(new Version(1, 1, 0), distributionClient.Downloads[0].Version);
            }
        }

        [Fact]
        public async Task UpdateService_UpdatesSymlinkToNewExecutable()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceBaseDirectory = "/opt";

                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockSecretsManager secretsManager = new MockSecretsManager();
                MockServiceManager serviceManager = new MockServiceManager();
                serviceManager.ServiceExistsStates["my-api"] = true;
                serviceManager.ServiceRunningStates["my-api"] = true;

                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                string currentSymlink = Path.Combine(serviceBaseDirectory, "my-api", "current");
                string oldExecutable = Path.Combine(serviceBaseDirectory, "my-api", "1.0.0", "my-api");
                symlinkManager.Symlinks[currentSymlink] = oldExecutable;

                MockExecutableDetector executableDetector = new MockExecutableDetector();
                string newVersionDirectory = Path.Combine(serviceBaseDirectory, "my-api", "1.1.0");
                string newExecutable = Path.Combine(newVersionDirectory, "my-api");
                executableDetector.SetExecutableResult(newVersionDirectory, "my-api", newExecutable);

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 1, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                versionExtractor.ExtractVersionFromPathResult = new Version(1, 0, 0);

                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    serviceManager,
                    symlinkManager,
                    executableDetector,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    filePermissionManager,
                    serviceBaseDirectory
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                // Should update symlink to point to version directory (not executable file)
                string expectedCall = $"CreateOrUpdateSymlinkAsync:{currentSymlink}:{newVersionDirectory}";
                Assert.Contains(symlinkManager.MethodCalls, call => call == expectedCall);
            }
        }

        [Fact]
        public async Task UpdateService_RestartsRunningService()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceBaseDirectory = "/opt";

                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockSecretsManager secretsManager = new MockSecretsManager();
                MockServiceManager serviceManager = new MockServiceManager();
                serviceManager.ServiceExistsStates["my-api"] = true;
                serviceManager.ServiceRunningStates["my-api"] = true;

                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                string currentSymlink = Path.Combine(serviceBaseDirectory, "my-api", "current");
                string oldExecutable = Path.Combine(serviceBaseDirectory, "my-api", "1.0.0", "my-api");
                symlinkManager.Symlinks[currentSymlink] = oldExecutable;

                MockExecutableDetector executableDetector = new MockExecutableDetector();
                string newVersionDirectory = Path.Combine(serviceBaseDirectory, "my-api", "1.1.0");
                string newExecutable = Path.Combine(newVersionDirectory, "my-api");
                executableDetector.SetExecutableResult(newVersionDirectory, "my-api", newExecutable);

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 1, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                versionExtractor.ExtractVersionFromPathResult = new Version(1, 0, 0);

                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    serviceManager,
                    symlinkManager,
                    executableDetector,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    filePermissionManager,
                    serviceBaseDirectory
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                // Should restart service
                Assert.Contains(serviceManager.MethodCalls, call => call == "RestartServiceAsync:my-api");
            }
        }

        [Fact]
        public async Task UpdateService_StartsStoppedService()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceBaseDirectory = "/opt";

                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockSecretsManager secretsManager = new MockSecretsManager();
                MockServiceManager serviceManager = new MockServiceManager();
                serviceManager.ServiceExistsStates["my-api"] = true;
                serviceManager.ServiceRunningStates["my-api"] = false;

                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                string currentSymlink = Path.Combine(serviceBaseDirectory, "my-api", "current");
                string oldExecutable = Path.Combine(serviceBaseDirectory, "my-api", "1.0.0", "my-api");
                symlinkManager.Symlinks[currentSymlink] = oldExecutable;

                MockExecutableDetector executableDetector = new MockExecutableDetector();
                string newVersionDirectory = Path.Combine(serviceBaseDirectory, "my-api", "1.1.0");
                string newExecutable = Path.Combine(newVersionDirectory, "my-api");
                executableDetector.SetExecutableResult(newVersionDirectory, "my-api", newExecutable);

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 1, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                versionExtractor.ExtractVersionFromPathResult = new Version(1, 0, 0);

                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    serviceManager,
                    symlinkManager,
                    executableDetector,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    filePermissionManager,
                    serviceBaseDirectory
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                // Should start (not restart) the stopped service
                Assert.Contains(serviceManager.MethodCalls, call => call == "StartServiceAsync:my-api");
                Assert.DoesNotContain(serviceManager.MethodCalls, call => call == "RestartServiceAsync:my-api");
            }
        }

        [Fact]
        public async Task UpdateService_MissingExecutable_DoesNotUpdateSymlink()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockSecretsManager secretsManager = new MockSecretsManager();
                MockServiceManager serviceManager = new MockServiceManager();
                MockSymlinkManager symlinkManager = new MockSymlinkManager();

                MockExecutableDetector executableDetector = new MockExecutableDetector();
                // Don't configure any result - detector will return null

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 0, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    serviceManager,
                    symlinkManager,
                    executableDetector,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    filePermissionManager
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                // Should not create symlink if executable not found
                Assert.DoesNotContain(symlinkManager.MethodCalls, call => call.StartsWith("CreateOrUpdateSymlinkAsync"));
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

                MockServiceManager serviceManager = new MockServiceManager();
                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                MockExecutableDetector executableDetector = new MockExecutableDetector();
                executableDetector.SetExecutableResult("/opt/my-api/1.0.0", "my-api", "/opt/my-api/1.0.0/my-api");

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 0, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    serviceManager,
                    symlinkManager,
                    executableDetector,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    filePermissionManager
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                // Should initialize with formatted secrets
                Assert.NotNull(distributionClient.InitializedSecrets);
                Assert.Contains("apiKey=abc123", distributionClient.InitializedSecrets);
                Assert.Contains("tenantId=550e8400", distributionClient.InitializedSecrets);
            }
        }

        [Fact]
        public async Task UpdateService_SetsFilePermissions()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceBaseDirectory = "/opt";

                MockConfigManager configManager = new MockConfigManager();
                string pluginPath = tempHelper.CreateTempFile("plugins/github/bin", "fake-plugin");
                InstalledPluginInfo pluginInfo = new InstalledPluginInfo { Alias = "github", Path = pluginPath };
                await configManager.AddOrUpdatePluginAsync(pluginInfo);
                await configManager.RegisterServiceAsync("my-api", "MyApi", "github");

                MockSecretsManager secretsManager = new MockSecretsManager();
                MockServiceManager serviceManager = new MockServiceManager();
                serviceManager.ServiceExistsStates["my-api"] = true;
                serviceManager.ServiceRunningStates["my-api"] = true;

                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                string currentSymlink = Path.Combine(serviceBaseDirectory, "my-api", "current");
                string oldExecutable = Path.Combine(serviceBaseDirectory, "my-api", "1.0.0", "my-api");
                symlinkManager.Symlinks[currentSymlink] = oldExecutable;

                MockExecutableDetector executableDetector = new MockExecutableDetector();
                string newVersionDirectory = Path.Combine(serviceBaseDirectory, "my-api", "1.1.0");
                string newExecutable = Path.Combine(newVersionDirectory, "my-api");
                executableDetector.SetExecutableResult(newVersionDirectory, "my-api", newExecutable);

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("MyApi", new Version(1, 1, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                versionExtractor.ExtractVersionFromPathResult = new Version(1, 0, 0);

                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    serviceManager,
                    symlinkManager,
                    executableDetector,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    filePermissionManager,
                    serviceBaseDirectory
                );

                int exitCode = await command.ExecuteAsync(new[] { "my-api" });
                Assert.Equal(0, exitCode);

                // Should set executable permissions on the downloaded executable
                Assert.Contains(newExecutable, filePermissionManager.ExecutablePermissionsCalls);

                // Should set directory permissions on the service directory
                string expectedServiceDirectory = Path.Combine(serviceBaseDirectory, "my-api");
                Assert.Contains(expectedServiceDirectory, filePermissionManager.DirectoryPermissionsCalls);
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

                MockServiceManager serviceManager = new MockServiceManager();
                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                MockExecutableDetector executableDetector = new MockExecutableDetector();
                executableDetector.SetExecutableResult("/opt/svc1/1.0.0", "svc1", "/opt/svc1/1.0.0/svc1");
                executableDetector.SetExecutableResult("/opt/svc2/1.0.0", "svc2", "/opt/svc2/1.0.0/svc2");

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                distributionClient.SetLatestVersion("Svc1", new Version(1, 0, 0));
                distributionClient.SetLatestVersion("Svc2", new Version(1, 0, 0));

                MockVersionExtractor versionExtractor = new MockVersionExtractor();
                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    serviceManager,
                    symlinkManager,
                    executableDetector,
                    distributionClient,
                    new MockOutputWriter(),
                    versionExtractor,
                    filePermissionManager
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
                // Register service without DistributionPluginAlias (old format)
                await configManager.RegisterServiceAsync("old-service", "OldService", "github");
                // Manually create a service without plugin alias by directly modifying config
                UpdaemonConfig config = await configManager.LoadConfigAsync();
                RegisteredService serviceWithoutAlias = config.Services.First();
                serviceWithoutAlias.DistributionPluginAlias = ""; // Clear the alias
                await configManager.SaveConfigAsync(config);

                MockSecretsManager secretsManager = new MockSecretsManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();

                UpdateCommand command = new UpdateCommand(
                    configManager,
                    secretsManager,
                    new MockServiceManager(),
                    new MockSymlinkManager(),
                    new MockExecutableDetector(),
                    distributionClient,
                    outputWriter,
                    new MockVersionExtractor(),
                    new MockFilePermissionManager()
                );

                int exitCode = await command.ExecuteAsync(Array.Empty<string>());
            Assert.Equal(1, exitCode);

                // Should output error about missing plugin alias
                Assert.Contains(outputWriter.Errors, e => e.Contains("does not have a distribution plugin assigned"));
                // Should not try to connect
                Assert.DoesNotContain(distributionClient.MethodCalls, c => c.StartsWith("ConnectAsync"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_PluginNotFound_SkipsWithError()
        {
            MockConfigManager configManager = new MockConfigManager();
            // Register service with non-existent plugin
            await configManager.RegisterServiceAsync("my-service", "MyService", "non-existent-plugin");

            MockSecretsManager secretsManager = new MockSecretsManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();

            UpdateCommand command = new UpdateCommand(
                configManager,
                secretsManager,
                new MockServiceManager(),
                new MockSymlinkManager(),
                new MockExecutableDetector(),
                distributionClient,
                outputWriter,
                new MockVersionExtractor(),
                new MockFilePermissionManager()
            );

            int exitCode = await command.ExecuteAsync(Array.Empty<string>());
            Assert.Equal(0, exitCode);

            // Should output error about plugin not found
            Assert.Contains(outputWriter.Errors, e => e.Contains("Plugin 'non-existent-plugin' not found"));
            // Should not try to connect
            Assert.DoesNotContain(distributionClient.MethodCalls, c => c.StartsWith("ConnectAsync"));
        }

        [Fact]
        public async Task ExecuteAsync_PluginExecutableNotFound_SkipsWithError()
        {
            MockConfigManager configManager = new MockConfigManager();
            await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "github", Path = "/nonexistent/plugin/path" });
            await configManager.RegisterServiceAsync("my-service", "MyService", "github");

            MockSecretsManager secretsManager = new MockSecretsManager();
            MockOutputWriter outputWriter = new MockOutputWriter();
            MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();

            UpdateCommand command = new UpdateCommand(
                configManager,
                secretsManager,
                new MockServiceManager(),
                new MockSymlinkManager(),
                new MockExecutableDetector(),
                distributionClient,
                outputWriter,
                new MockVersionExtractor(),
                new MockFilePermissionManager()
            );

            int exitCode = await command.ExecuteAsync(Array.Empty<string>());
            Assert.Equal(0, exitCode);

            // Should output error about plugin executable not found
            Assert.Contains(outputWriter.Errors, e => e.Contains("Plugin executable not found"));
            // Should not try to connect
            Assert.DoesNotContain(distributionClient.MethodCalls, c => c.StartsWith("ConnectAsync"));
        }
    }
}

