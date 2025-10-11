using Updaemon.Common;
using Updaemon.Common.Utilities;
using Updaemon.GithubDistributionService.Services;

namespace Updaemon.GithubDistributionService.Tests
{
    [Trait("Category", "Integration")]
    public class GithubDistributionServiceIntegrationTests
    {
        [Fact]
        public async Task GetLatestVersionAsync_WithRealCurlRepo_ReturnsVersion()
        {
            GithubApiClient apiClient = new GithubApiClient();
            VersionParser versionParser = new VersionParser();
            DownloadPostProcessor postProcessor = new DownloadPostProcessor();
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            await service.InitializeAsync(new SecretCollection(new Dictionary<string, string>()));
            Version? version = await service.GetLatestVersionAsync("curl/curl");

            // We can't assert exact version since it changes, but we can verify it returns something
            Assert.NotNull(version);
            Assert.True(version.Major >= 7);
        }

        [Fact]
        public async Task GetLatestVersionAsync_WithNonExistentRepo_ReturnsNull()
        {
            GithubApiClient apiClient = new GithubApiClient();
            VersionParser versionParser = new VersionParser();
            DownloadPostProcessor postProcessor = new DownloadPostProcessor();
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            await service.InitializeAsync(new SecretCollection(new Dictionary<string, string>()));
            Version? version = await service.GetLatestVersionAsync("nonexistent-user-12345/nonexistent-repo-67890");

            Assert.Null(version);
        }

        [Fact]
        public async Task DownloadVersionAsync_WithRealRepo_DownloadsSuccessfully()
        {
            GithubApiClient apiClient = new GithubApiClient();
            VersionParser versionParser = new VersionParser();
            DownloadPostProcessor postProcessor = new DownloadPostProcessor();
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);
            string targetPath = Path.Combine(Path.GetTempPath(), $"updaemon_integration_test_{Guid.NewGuid():N}");

            try
            {
                await service.InitializeAsync(new SecretCollection(new Dictionary<string, string>()));
                
                // Get the latest version first
                Version? version = await service.GetLatestVersionAsync("adamtovatt/netlifydnsmanager");
                
                if (version != null)
                {
                    // Download using wildcard pattern (use more specific pattern to match only one)
                    await service.DownloadVersionAsync("adamtovatt/netlifydnsmanager/linux-arm.zip", version, targetPath);

                    // Verify files were downloaded and extracted
                    Assert.True(Directory.Exists(targetPath));
                    string[] files = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories);
                    Assert.NotEmpty(files);
                }
                else
                {
                    // Skip test if no release exists
                    Assert.True(true, "No release found, skipping download test");
                }
            }
            finally
            {
                if (Directory.Exists(targetPath))
                {
                    try
                    {
                        Directory.Delete(targetPath, true);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }

        [Fact]
        public async Task FullFlow_GetVersionThenDownload_WorksCorrectly()
        {
            GithubApiClient apiClient = new GithubApiClient();
            VersionParser versionParser = new VersionParser();
            DownloadPostProcessor postProcessor = new DownloadPostProcessor();
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);
            string targetPath = Path.Combine(Path.GetTempPath(), $"updaemon_integration_test_{Guid.NewGuid():N}");

            try
            {
                await service.InitializeAsync(new SecretCollection(new Dictionary<string, string>()));
                
                // This tests the full flow that updaemon would use
                Version? latestVersion = await service.GetLatestVersionAsync("curl/curl");
                
                if (latestVersion != null)
                {
                    // Download using wildcard pattern to get the .zip file
                    await service.DownloadVersionAsync("curl/curl/*.zip", latestVersion, targetPath);
                    
                    Assert.True(Directory.Exists(targetPath));
                    
                    // After post-processing, the zip should be extracted
                    string[] allFiles = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories);
                    Assert.NotEmpty(allFiles);
                    Assert.True(allFiles.Length > 5);
                }
                else
                {
                    Assert.Fail("curl/curl should have a latest release");
                }
            }
            finally
            {
                if (Directory.Exists(targetPath))
                {
                    try
                    {
                        Directory.Delete(targetPath, true);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }
    }
}
