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
            PluginInstaller installer = new PluginInstaller(resolver, downloader, configManager, TestScope.Create(tempHelper.TempDirectory));
            return new DistInstallCommand(outputWriter, installer, UpdaemonVersion.Of(0, 11, 0));
        }

        [Theory]
        [MemberData(nameof(Arguments))]
        public async Task ExecuteAsync_NameOrUrl_InstallsFromTheRegistryAtTheRunningVersionOrFromTheUrl(string argument, PluginSource expectedSource, string expectedAlias, string expectedDownloadUrl)
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
                Assert.Contains("Distribution plugin 'github' replaced successfully", outputWriter.Messages);
            }
        }
    }
}
