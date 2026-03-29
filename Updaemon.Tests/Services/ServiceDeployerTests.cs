using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    public class ServiceDeployerTests
    {
        [Fact]
        public async Task DeployVersionAsync_HappyPath_DownloadsAndCreatesSymlink()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceBaseDirectory = tempHelper.TempDirectory;

                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                MockExecutableDetector executableDetector = new MockExecutableDetector();
                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();

                RegisteredService service = new RegisteredService
                {
                    LocalName = "my-api",
                    RemoteName = "MyApi",
                    DistributionPluginAlias = "github",
                };

                Version version = new Version(1, 0, 0);
                string versionDir = Path.Combine(serviceBaseDirectory, "my-api", "1.0.0");
                string execPath = Path.Combine(versionDir, "my-api");
                executableDetector.SetExecutableResult(versionDir, "my-api", execPath);

                ServiceDeployer deployer = new ServiceDeployer(
                    symlinkManager, executableDetector, filePermissionManager, outputWriter, serviceBaseDirectory);

                DeployResult? result = await deployer.DeployVersionAsync(service, version, distributionClient);

                Assert.NotNull(result);
                Assert.Equal(versionDir, result.VersionDirectory);
                Assert.Equal(execPath, result.ExecutablePath);
                Assert.Equal(Path.Combine(serviceBaseDirectory, "my-api", "current"), result.SymlinkPath);

                // Verify download was called
                Assert.Single(distributionClient.Downloads);
                Assert.Equal("MyApi", distributionClient.Downloads[0].ServiceName);
                Assert.Equal(version, distributionClient.Downloads[0].Version);

                // Verify permissions were set
                Assert.Contains(execPath, filePermissionManager.ExecutablePermissionsCalls);
                Assert.Contains(Path.Combine(serviceBaseDirectory, "my-api"), filePermissionManager.DirectoryPermissionsCalls);

                // Verify symlink was created
                string expectedSymlinkCall = $"CreateOrUpdateSymlinkAsync:{result.SymlinkPath}:{versionDir}";
                Assert.Contains(symlinkManager.MethodCalls, c => c == expectedSymlinkCall);
            }
        }

        [Fact]
        public async Task DeployVersionAsync_ExecutableNotFound_ReturnsNull()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceBaseDirectory = tempHelper.TempDirectory;

                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                MockExecutableDetector executableDetector = new MockExecutableDetector();
                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();

                RegisteredService service = new RegisteredService
                {
                    LocalName = "my-api",
                    RemoteName = "MyApi",
                    DistributionPluginAlias = "github",
                };
                // Don't configure executable detector — returns null

                ServiceDeployer deployer = new ServiceDeployer(
                    symlinkManager, executableDetector, filePermissionManager, outputWriter, serviceBaseDirectory);

                DeployResult? result = await deployer.DeployVersionAsync(service, new Version(1, 0, 0), distributionClient);

                Assert.Null(result);
                Assert.Contains(outputWriter.Errors, e => e.Contains("Could not find executable"));
                // Should not create symlink
                Assert.DoesNotContain(symlinkManager.MethodCalls, c => c.StartsWith("CreateOrUpdateSymlinkAsync"));
            }
        }

        [Fact]
        public async Task DeployVersionAsync_UsesCustomExecutableName()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceBaseDirectory = tempHelper.TempDirectory;

                MockSymlinkManager symlinkManager = new MockSymlinkManager();
                MockExecutableDetector executableDetector = new MockExecutableDetector();
                MockFilePermissionManager filePermissionManager = new MockFilePermissionManager();
                MockOutputWriter outputWriter = new MockOutputWriter();
                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();

                RegisteredService service = new RegisteredService
                {
                    LocalName = "my-api",
                    RemoteName = "MyApi",
                    ExecutableName = "CustomExec",
                    DistributionPluginAlias = "github",
                };

                Version version = new Version(1, 0, 0);
                string versionDir = Path.Combine(serviceBaseDirectory, "my-api", "1.0.0");
                string execPath = Path.Combine(versionDir, "CustomExec");
                executableDetector.SetExecutableResult(versionDir, "CustomExec", execPath);

                ServiceDeployer deployer = new ServiceDeployer(
                    symlinkManager, executableDetector, filePermissionManager, outputWriter, serviceBaseDirectory);

                DeployResult? result = await deployer.DeployVersionAsync(service, version, distributionClient);

                Assert.NotNull(result);
                Assert.Equal(execPath, result.ExecutablePath);
            }
        }

        [Fact]
        public void GetSymlinkPath_ReturnsExpectedPath()
        {
            ServiceDeployer deployer = new ServiceDeployer(
                new MockSymlinkManager(), new MockExecutableDetector(),
                new MockFilePermissionManager(), new MockOutputWriter(), "/opt");

            Assert.Equal("/opt/my-api/current", deployer.GetSymlinkPath("my-api"));
        }

        [Fact]
        public async Task ReadCurrentTargetAsync_WhenInitialized_ReturnsTarget()
        {
            MockSymlinkManager symlinkManager = new MockSymlinkManager();
            symlinkManager.Symlinks["/opt/my-api/current"] = "/opt/my-api/1.0.0";

            ServiceDeployer deployer = new ServiceDeployer(
                symlinkManager, new MockExecutableDetector(),
                new MockFilePermissionManager(), new MockOutputWriter(), "/opt");

            string? target = await deployer.ReadCurrentTargetAsync("my-api");

            Assert.Equal("/opt/my-api/1.0.0", target);
        }

        [Fact]
        public async Task ReadCurrentTargetAsync_WhenNotInitialized_ReturnsNull()
        {
            MockSymlinkManager symlinkManager = new MockSymlinkManager();

            ServiceDeployer deployer = new ServiceDeployer(
                symlinkManager, new MockExecutableDetector(),
                new MockFilePermissionManager(), new MockOutputWriter(), "/opt");

            string? target = await deployer.ReadCurrentTargetAsync("my-api");

            Assert.Null(target);
        }

        [Fact]
        public async Task CleanupDeployAsync_RemovesVersionDirectory()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceBaseDirectory = tempHelper.TempDirectory;
                string versionDir = Path.Combine(serviceBaseDirectory, "my-api", "1.0.0");
                Directory.CreateDirectory(versionDir);
                File.WriteAllText(Path.Combine(versionDir, "dummy"), "test");

                MockSymlinkManager symlinkManager = new MockSymlinkManager();

                ServiceDeployer deployer = new ServiceDeployer(
                    symlinkManager, new MockExecutableDetector(),
                    new MockFilePermissionManager(), new MockOutputWriter(), serviceBaseDirectory);

                DeployResult result = new DeployResult
                {
                    VersionDirectory = versionDir,
                    ExecutablePath = Path.Combine(versionDir, "my-api"),
                    SymlinkPath = Path.Combine(serviceBaseDirectory, "my-api", "current"),
                };

                await deployer.CleanupDeployAsync(result);

                Assert.False(Directory.Exists(versionDir));
            }
        }
    }
}
