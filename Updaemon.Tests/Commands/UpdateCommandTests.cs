using Updaemon.Commands;
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

            await command.ExecuteAsync();

            // Should not connect to distribution client when no plugin configured
            Assert.DoesNotContain(distributionClient.MethodCalls, call => call.StartsWith("ConnectAsync"));
        }

        [Fact]
        public async Task ExecuteAsync_SpecificServiceNotRegistered_DoesNotProceed()
        {
            MockConfigManager configManager = new MockConfigManager();
            await configManager.SetDistributionPluginPathAsync("/path/to/plugin");

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

            await command.ExecuteAsync("non-existent-service");

            // Should not try to get latest version if service not registered
            Assert.DoesNotContain(distributionClient.MethodCalls, call => call.StartsWith("GetLatestVersionAsync"));
        }

        [Fact]
        public async Task ExecuteAsync_NoServicesRegistered_DoesNotProceed()
        {
            MockConfigManager configManager = new MockConfigManager();
            await configManager.SetDistributionPluginPathAsync("/path/to/plugin");

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

            await command.ExecuteAsync();

            // Should not try to get latest version if no services registered
            Assert.DoesNotContain(distributionClient.MethodCalls, call => call.StartsWith("GetLatestVersionAsync"));
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesSpecificServiceWhenAppNameProvided()
        {
            MockConfigManager configManager = new MockConfigManager();
            await configManager.SetDistributionPluginPathAsync("/path/to/plugin");
            await configManager.RegisterServiceAsync("my-api", "MyApi");

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

            await command.ExecuteAsync("my-api");

            Assert.Contains(distributionClient.MethodCalls, call => call == "GetLatestVersionAsync:MyApi");
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesAllServicesWhenNoAppNameProvided()
        {
            MockConfigManager configManager = new MockConfigManager();
            await configManager.SetDistributionPluginPathAsync("/path/to/plugin");
            await configManager.RegisterServiceAsync("service1", "Service1");
            await configManager.RegisterServiceAsync("service2", "Service2");

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

            await command.ExecuteAsync();

            Assert.Contains(distributionClient.MethodCalls, call => call == "GetLatestVersionAsync:Service1");
            Assert.Contains(distributionClient.MethodCalls, call => call == "GetLatestVersionAsync:Service2");
        }

        [Fact]
        public async Task UpdateService_AlreadyUpToDate_SkipsUpdate()
        {
            string serviceBaseDirectory = "/opt";

            MockConfigManager configManager = new MockConfigManager();
            await configManager.SetDistributionPluginPathAsync("/path/to/plugin");
            await configManager.RegisterServiceAsync("my-api", "MyApi");

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

            await command.ExecuteAsync("my-api");

            // Should not download if already up to date
            Assert.Empty(distributionClient.Downloads);

            // Should not call any service manager methods (no restart/start)
            Assert.Empty(serviceManager.MethodCalls.Where(call =>
                call.Contains("Start") || call.Contains("Restart") || call.Contains("Stop")));
        }

        [Fact]
        public async Task UpdateService_NewerVersionAvailable_DownloadsAndInstalls()
        {
            string serviceBaseDirectory = "/opt";

            MockConfigManager configManager = new MockConfigManager();
            await configManager.SetDistributionPluginPathAsync("/path/to/plugin");
            await configManager.RegisterServiceAsync("my-api", "MyApi");

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

            await command.ExecuteAsync("my-api");

            // Should download new version
            Assert.Single(distributionClient.Downloads);
            Assert.Equal("MyApi", distributionClient.Downloads[0].ServiceName);
            Assert.Equal(new Version(1, 1, 0), distributionClient.Downloads[0].Version);
        }

        [Fact]
        public async Task UpdateService_UpdatesSymlinkToNewExecutable()
        {
            string serviceBaseDirectory = "/opt";

            MockConfigManager configManager = new MockConfigManager();
            await configManager.SetDistributionPluginPathAsync("/path/to/plugin");
            await configManager.RegisterServiceAsync("my-api", "MyApi");

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

            await command.ExecuteAsync("my-api");

            // Should update symlink
            string expectedCall = $"CreateOrUpdateSymlinkAsync:{currentSymlink}:{newExecutable}";
            Assert.Contains(symlinkManager.MethodCalls, call => call == expectedCall);
        }

        [Fact]
        public async Task UpdateService_RestartsRunningService()
        {
            string serviceBaseDirectory = "/opt";

            MockConfigManager configManager = new MockConfigManager();
            await configManager.SetDistributionPluginPathAsync("/path/to/plugin");
            await configManager.RegisterServiceAsync("my-api", "MyApi");

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

            await command.ExecuteAsync("my-api");

            // Should restart service
            Assert.Contains(serviceManager.MethodCalls, call => call == "RestartServiceAsync:my-api");
        }

        [Fact]
        public async Task UpdateService_StartsStoppedService()
        {
            string serviceBaseDirectory = "/opt";

            MockConfigManager configManager = new MockConfigManager();
            await configManager.SetDistributionPluginPathAsync("/path/to/plugin");
            await configManager.RegisterServiceAsync("my-api", "MyApi");

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

            await command.ExecuteAsync("my-api");

            // Should start (not restart) the stopped service
            Assert.Contains(serviceManager.MethodCalls, call => call == "StartServiceAsync:my-api");
            Assert.DoesNotContain(serviceManager.MethodCalls, call => call == "RestartServiceAsync:my-api");
        }

        [Fact]
        public async Task UpdateService_MissingExecutable_DoesNotUpdateSymlink()
        {
            MockConfigManager configManager = new MockConfigManager();
            await configManager.SetDistributionPluginPathAsync("/path/to/plugin");
            await configManager.RegisterServiceAsync("my-api", "MyApi");

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

            await command.ExecuteAsync("my-api");

            // Should not create symlink if executable not found
            Assert.DoesNotContain(symlinkManager.MethodCalls, call => call.StartsWith("CreateOrUpdateSymlinkAsync"));
        }

        [Fact]
        public async Task UpdateService_InitializesDistributionClientWithSecrets()
        {
            MockConfigManager configManager = new MockConfigManager();
            await configManager.SetDistributionPluginPathAsync("/path/to/plugin");
            await configManager.RegisterServiceAsync("my-api", "MyApi");

            MockSecretsManager secretsManager = new MockSecretsManager();
            await secretsManager.SetSecretAsync("apiKey", "abc123");
            await secretsManager.SetSecretAsync("tenantId", "550e8400");

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

            await command.ExecuteAsync("my-api");

            // Should initialize with formatted secrets
            Assert.NotNull(distributionClient.InitializedSecrets);
            Assert.Contains("apiKey=abc123", distributionClient.InitializedSecrets);
            Assert.Contains("tenantId=550e8400", distributionClient.InitializedSecrets);
        }

        [Fact]
        public async Task UpdateService_SetsFilePermissions()
        {
            string serviceBaseDirectory = "/opt";

            MockConfigManager configManager = new MockConfigManager();
            await configManager.SetDistributionPluginPathAsync("/path/to/plugin");
            await configManager.RegisterServiceAsync("my-api", "MyApi");

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

            await command.ExecuteAsync("my-api");

            // Should set executable permissions on the downloaded executable
            Assert.Contains(newExecutable, filePermissionManager.ExecutablePermissionsCalls);

            // Should set directory permissions on the service directory
            string expectedServiceDirectory = Path.Combine(serviceBaseDirectory, "my-api");
            Assert.Contains(expectedServiceDirectory, filePermissionManager.DirectoryPermissionsCalls);
        }
    }
}

