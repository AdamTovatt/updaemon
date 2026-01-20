using EasyReasy.EnvironmentVariables;
using Updaemon.Common;
using Updaemon.Common.Utilities;
using Updaemon.GithubDistributionService.Services;
using Xunit.Sdk;

namespace Updaemon.GithubDistributionService.Tests
{
    [Trait("Category", "Integration")]
    public class GithubDistributionServicePrivateRepoIntegrationTests
    {
        [Fact]
        public async Task DownloadVersionAsync_WithPrivateRepo_WorksWhenConfigured()
        {
            TryLoadPrivateGithubTestConfiguration();

            try
            {
                EnvironmentVariableHelper.ValidateVariableNamesIn(typeof(PrivateGithubTestEnvironmentVariable));
            }
            catch (InvalidOperationException ex)
            {
                throw new SkipException(ex.Message);
            }

            string remote = PrivateGithubTestEnvironmentVariable.Remote.GetValue();
            string token = PrivateGithubTestEnvironmentVariable.Token.GetValue();

            GithubApiClient apiClient = new GithubApiClient();
            VersionParser versionParser = new VersionParser();
            DownloadPostProcessor postProcessor = new DownloadPostProcessor();
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            SecretCollection secrets = SecretCollection.FromString($"githubToken={token}");
            await service.InitializeAsync(secrets);

            Version? latestVersion = await service.GetLatestVersionAsync(remote);
            if (latestVersion == null)
                throw new InvalidOperationException($"No latest release found for '{remote}'.");

            string targetPath = Path.Combine(Path.GetTempPath(), $"updaemon_private_repo_integration_test_{Guid.NewGuid():N}");

            try
            {
                await service.DownloadVersionAsync(remote, latestVersion, targetPath);

                Assert.True(Directory.Exists(targetPath));
                string[] files = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories);
                Assert.NotEmpty(files);
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

        private static void TryLoadPrivateGithubTestConfiguration()
        {
            string envFilePath = Path.Combine("..", "..", "EnvironmentVariables.txt");
            if (!File.Exists(envFilePath))
            {
                EnvironmentVariableHelper.WriteExampleFile(
                    envFilePath,
                    PrivateGithubTestEnvironmentVariable.Remote.Name,
                    "owner/repo/asset-pattern",
                    PrivateGithubTestEnvironmentVariable.Token.Name,
                    "ghp_your_token_here");

                throw new SkipException($"Created '{envFilePath}'. Populate it and re-run the integration test.");
            }

            EnvironmentVariableHelper.LoadVariablesFromFile(envFilePath);
        }
    }
}

