using Updaemon.Common.Models;
using Updaemon.Common.Utilities;
using Updaemon.Configuration;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    public class PluginInstallerTests
    {
        private static readonly UpdaemonVersion RunningRelease = UpdaemonVersion.Of(1, 2, 0);

        private static GitHubPluginUrlResolver Resolver()
        {
            return new GitHubPluginUrlResolver(
                new Dictionary<string, string> { ["github"] = "Updaemon.GithubDistributionService" },
                new HostPlatform(false, "linux-arm64"));
        }

        private static PluginInstaller Installer(TempFileHelper tempHelper, MockPluginDownloader downloader, MockConfigManager configManager)
        {
            return new PluginInstaller(Resolver(), new MockGitHubPluginReleases(), downloader, configManager, TestScope.Create(tempHelper.TempDirectory));
        }

        // Where TestScope puts the plugins directory, relative to the temp root.
        private const string PluginsRelativePath = "root/plugins";

        [Theory]
        [InlineData(null, "github")]
        [InlineData("custom", "custom")]
        public async Task InstallAsync_RegistryPlugin_DownloadsFromThisUpdaemonsReleaseAndRecordsTheName(string? alias, string expectedAlias)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockPluginDownloader downloader = new MockPluginDownloader();
                string pluginsDirectory = tempHelper.GetFullPath(PluginsRelativePath);
                PluginInstaller installer = Installer(tempHelper, downloader, configManager);

                PluginInstallResult result = await installer.InstallAsync(new PluginSource.Registry("github", RunningRelease), alias);

                Assert.Equal(
                    ("https://github.com/AdamTovatt/updaemon/releases/download/v1.2.0/Updaemon.GithubDistributionService-linux-arm64", true),
                    Assert.Single(downloader.Downloads));
                InstalledPluginInfo? recorded = await configManager.GetPluginAsync(expectedAlias);
                Assert.NotNull(recorded);
                Assert.Equal(new PluginSource.Registry("github", RunningRelease), recorded.Source);
                Assert.Equal(Path.Combine(pluginsDirectory, expectedAlias, "Updaemon.GithubDistributionService-linux-arm64"), recorded.Path);
                Assert.True(File.Exists(recorded.Path));
                Assert.False(result.ReplacedExisting);
            }
        }

        [Theory]
        [InlineData(null, "mock")]
        [InlineData("custom", "custom")]
        public async Task InstallAsync_UrlPlugin_InstallsUnderTheGivenOrDefaultAliasAndRecordsTheUrl(string? alias, string expectedAlias)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginsDirectory = tempHelper.GetFullPath(PluginsRelativePath);
                MockPluginDownloader downloader = new MockPluginDownloader();
                PluginInstaller installer = Installer(tempHelper, downloader, configManager);
                PluginSource source = new PluginSource.Url("https://example.com/path/to/my-plugin");

                await installer.InstallAsync(source, alias);

                Assert.Equal(("https://example.com/path/to/my-plugin", false), Assert.Single(downloader.Downloads));
                InstalledPluginInfo? recorded = await configManager.GetPluginAsync(expectedAlias);
                Assert.NotNull(recorded);
                Assert.Equal(source, recorded.Source);
                Assert.Equal(Path.Combine(pluginsDirectory, expectedAlias, "my-plugin"), recorded.Path);
            }
        }

        [Fact]
        public async Task InstallAsync_GitHubPlugin_DownloadsItsReleasesBuildVerifiedAndRecordsTheTag()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockPluginDownloader downloader = new MockPluginDownloader();
                PluginSource source = new PluginSource.GitHub("someone", "updaemon-s3", "v2.1.0");

                await Installer(tempHelper, downloader, configManager).InstallAsync(source, alias: null);

                Assert.Equal((MockGitHubPluginReleases.BuildUrl("someone", "updaemon-s3", "v2.1.0"), true), Assert.Single(downloader.Downloads));
                Assert.Equal(source, (await configManager.GetPluginAsync("mock"))!.Source);
            }
        }

        [Fact]
        public async Task InstallAsync_NameNotInRegistry_FailsBeforeDownloadingAndPointsToUrlInstall()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockPluginDownloader downloader = new MockPluginDownloader();
                PluginInstaller installer = Installer(tempHelper, downloader, new MockConfigManager());

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => installer.InstallAsync(new PluginSource.Registry("gitlab", RunningRelease), null));

                Assert.Equal(
                    "'gitlab' is not in the plugin registry (github). Install it from its URL with 'updaemon dist-install <url>'.",
                    exception.Message);
                Assert.Empty(downloader.MethodCalls);
            }
        }

        [Theory]
        [InlineData("Updaemon.GithubDistributionService-linux-arm64")]
        [InlineData("old-plugin-binary")]
        public async Task InstallAsync_AliasAlreadyInstalled_ReplacesItWithTheNewBinaryAndLeavesNoOldOne(string installedFileName)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginsDirectory = tempHelper.GetFullPath(PluginsRelativePath);
                string installedPath = tempHelper.CreateTempFile($"{PluginsRelativePath}/github/{installedFileName}", "old");
                await configManager.AddOrUpdatePluginAsync(PluginRecords.Installed("github", installedPath));
                PluginInstaller installer = Installer(tempHelper, new MockPluginDownloader(), configManager);

                PluginInstallResult result = await installer.InstallAsync(new PluginSource.Registry("github", RunningRelease), null);

                Assert.True(result.ReplacedExisting);
                InstalledPluginInfo? recorded = await configManager.GetPluginAsync("github");
                Assert.NotNull(recorded);
                Assert.Equal(MockPluginDownloader.PluginBytes, File.ReadAllBytes(recorded.Path));
                Assert.Equal(recorded.Path, Assert.Single(Directory.GetFiles(pluginsDirectory, "*", SearchOption.AllDirectories)));
            }
        }

        [Fact]
        public async Task InstallAsync_ReleaseHasNoBuildForThisMachine_NamesTheMissingBuild()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockPluginDownloader downloader = new MockPluginDownloader
                {
                    ExceptionToThrow = new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, System.Net.HttpStatusCode.NotFound),
                };
                PluginInstaller installer = Installer(tempHelper, downloader, new MockConfigManager());

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => installer.InstallAsync(new PluginSource.Registry("github", RunningRelease), null));

                Assert.Equal(
                    "Updaemon's release has no 'github' plugin built for this machine: " +
                    "https://github.com/AdamTovatt/updaemon/releases/download/v1.2.0/Updaemon.GithubDistributionService-linux-arm64 was not found. " +
                    "Build the plugin for this machine and install it with 'updaemon dist-install <url>'.",
                    exception.Message);
            }
        }

        [Fact]
        public async Task InstallAsync_NewPluginFailsItsChecksum_KeepsTheInstalledPlugin()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string installedPath = tempHelper.CreateTempFile($"{PluginsRelativePath}/github/Updaemon.GithubDistributionService-linux-arm64", "installed");
                InstalledPluginInfo installed = PluginRecords.Installed("github", installedPath);
                await configManager.AddOrUpdatePluginAsync(installed);
                MockPluginDownloader downloader = new MockPluginDownloader
                {
                    ExceptionToThrow = new ChecksumException("Updaemon.GithubDistributionService-linux-arm64 does not match its SHA-256 in SHA256SUMS."),
                };

                await Assert.ThrowsAsync<ChecksumException>(
                    () => Installer(tempHelper, downloader, configManager).InstallAsync(new PluginSource.Registry("github", RunningRelease), null));

                Assert.Equal("installed", File.ReadAllText(installedPath));
                Assert.Same(installed, await configManager.GetPluginAsync("github"));
            }
        }

        [Fact]
        public async Task InstallAsync_NewPluginFailsItsSmokeRun_KeepsTheInstalledPlugin()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string installedPath = tempHelper.CreateTempFile($"{PluginsRelativePath}/github/Updaemon.GithubDistributionService-linux-arm64", "installed");
                byte[] installedBytes = File.ReadAllBytes(installedPath);
                InstalledPluginInfo installed = PluginRecords.Installed("github", installedPath);
                await configManager.AddOrUpdatePluginAsync(installed);
                MockPluginDownloader downloader = new MockPluginDownloader
                {
                    ExceptionToThrow = new PluginException("speaks plugin protocol 1"),
                };
                PluginInstaller installer = Installer(tempHelper, downloader, configManager);

                await Assert.ThrowsAsync<PluginException>(
                    () => installer.InstallAsync(new PluginSource.Registry("github", RunningRelease), null));

                Assert.Equal(installedBytes, File.ReadAllBytes(installedPath));
                Assert.Same(installed, await configManager.GetPluginAsync("github"));
            }
        }

        [Fact]
        public async Task InstallAsync_UrlPluginWithoutDefaultAliasAndNoneGiven_FailsAndLeavesNoFiles()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                string pluginsDirectory = tempHelper.GetFullPath(PluginsRelativePath);
                MockPluginDownloader downloader = new MockPluginDownloader
                {
                    ServiceInformation = new DistributionServiceInformation { FullName = "Test Plugin", DefaultAlias = "", Version = "1.0.0" },
                };
                PluginInstaller installer = Installer(tempHelper, downloader, configManager);

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => installer.InstallAsync(new PluginSource.Url("https://example.com/plugin"), null));

                Assert.Contains("--as <alias>", exception.Message);
                Assert.Empty(await configManager.GetAllPluginsAsync());
                Assert.Empty(Directory.GetFiles(pluginsDirectory, "*", SearchOption.AllDirectories));
            }
        }
    }
}
