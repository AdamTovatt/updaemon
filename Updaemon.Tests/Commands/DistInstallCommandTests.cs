using Updaemon.Commands;
using Updaemon.Configuration;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class DistInstallCommandTests
    {
        public static TheoryData<string, PluginSource, string, string> Arguments => new TheoryData<string, PluginSource, string, string>
        {
            {
                "github",
                new PluginSource.Registry("github", UpdaemonVersion.Of(0, 11, 0)),
                "github",
                "https://github.com/AdamTovatt/updaemon/releases/download/v0.11.0/Updaemon.GithubDistributionService-osx-arm64"
            },
            {
                "github:someone/updaemon-s3",
                new PluginSource.GitHub("someone", "updaemon-s3", "v2.1.0"),
                "mock",
                MockGitHubPluginReleases.BuildUrl("someone", "updaemon-s3", "v2.1.0")
            },
            {
                "https://example.com/path/to/plugin-bin",
                new PluginSource.Url("https://example.com/path/to/plugin-bin"),
                "mock",
                "https://example.com/path/to/plugin-bin"
            },
        };

        private static DistInstallCommand Command(TempFileHelper tempHelper, MockConfigManager configManager, MockPluginDownloader downloader, MockOutputWriter outputWriter)
        {
            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(
                new Dictionary<string, string> { ["github"] = "Updaemon.GithubDistributionService" },
                new HostPlatform(true, "osx-arm64"));
            MockGitHubPluginReleases gitHubPlugins = new MockGitHubPluginReleases();
            gitHubPlugins.LatestTags["someone/updaemon-s3"] = "v2.1.0";
            PluginInstaller installer = new PluginInstaller(resolver, gitHubPlugins, downloader, configManager, TestScope.Create(tempHelper.TempDirectory));
            return new DistInstallCommand(
                outputWriter, installer, gitHubPlugins, UpdaemonVersion.Of(0, 11, 0), new PluginCredentials(new MockSecretsManager(), new MockPrompter(), outputWriter));
        }

        [Theory]
        [MemberData(nameof(Arguments))]
        public async Task ExecuteAsync_NameRepositoryOrUrl_InstallsFromTheRegistryTheLatestReleaseOrTheUrl(string argument, PluginSource expectedSource, string expectedAlias, string expectedDownloadUrl)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockPluginDownloader downloader = new MockPluginDownloader();

                int exitCode = await Command(tempHelper, configManager, downloader, new MockOutputWriter()).ExecuteAsync(new[] { argument });

                Assert.Equal(0, exitCode);
                Assert.Equal($"DownloadAndInspectAsync:{expectedDownloadUrl}", Assert.Single(downloader.MethodCalls));
                InstalledPluginInfo? recorded = await configManager.GetPluginAsync(expectedAlias);
                Assert.NotNull(recorded);
                Assert.Equal(expectedSource, recorded.Source);
            }
        }

        [Fact]
        public async Task ExecuteAsync_AliasAlreadyInstalled_SaysItWasReplaced()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string installedPath = tempHelper.CreateTempFile("plugins/github/old-plugin", "old");
                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("github", installedPath));
                MockOutputWriter outputWriter = new MockOutputWriter();

                int exitCode = await Command(tempHelper, configManager, new MockPluginDownloader(), outputWriter).ExecuteAsync(new[] { "github" });

                Assert.Equal(0, exitCode);
                Assert.Contains("Replaced the 'github' plugin.", outputWriter.Messages);
            }
        }

        [Theory]
        [InlineData("github:someone")]
        [InlineData("github:someone/repo/extra")]
        [InlineData("github:/repo")]
        [InlineData("github:someone/..")]
        [InlineData("github:some one/repo")]
        [InlineData("github:someone/repo?x=1")]
        public async Task ExecuteAsync_GitHubSourceThatIsNotOwnerAndRepo_FailsWithoutInstalling(string argument)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockPluginDownloader downloader = new MockPluginDownloader();
                MockOutputWriter outputWriter = new MockOutputWriter();

                int exitCode = await Command(tempHelper, new MockConfigManager(), downloader, outputWriter).ExecuteAsync(new[] { argument });

                Assert.Equal(1, exitCode);
                Assert.Equal($"Error: '{argument}' is not github:<owner>/<repo>, each of letters, digits, '.', '_' and '-'.", Assert.Single(outputWriter.Errors));
                Assert.Empty(downloader.MethodCalls);
            }
        }

        [Fact]
        public async Task ExecuteAsync_GitHubRepositoryWithoutARelease_FailsWithoutDownloading()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockPluginDownloader downloader = new MockPluginDownloader();

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => Command(tempHelper, new MockConfigManager(), downloader, new MockOutputWriter()).ExecuteAsync(new[] { "github:someone/no-releases" }));

                Assert.Equal("someone/no-releases has no release on GitHub.", ex.Message);
                Assert.Empty(downloader.MethodCalls);
            }
        }
    }
}
