using Updaemon.Common.Utilities;
using Updaemon.Configuration;
using Updaemon.Models;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    /// <summary>
    /// A registry plugin always matches the running updaemon's release before a command runs it.
    /// </summary>
    public class PluginProviderTests
    {
        private static readonly UpdaemonVersion OlderRelease = UpdaemonVersion.Of(0, 9, 0);

        [Fact]
        public async Task GetOrInstallAsync_RegistryPluginFromAnotherRelease_ReinstallsItFromTheRunningOne()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.Config.AddOrUpdatePluginAsync(PluginRecords.Installed("github", "/old/plugin", OlderRelease));

                InstalledPluginInfo plugin = await h.Plugins.GetOrInstallAsync("github");

                Assert.Equal(new PluginSource.Registry("github", AppHarness.RunningVersion), plugin.Source);
                Assert.Equal(new PluginSource.Registry("github", AppHarness.RunningVersion), (await h.Config.GetPluginAsync("github"))!.Source);
                Assert.Contains(h.PluginDownloader.MethodCalls, call => call.Contains("/download/v1.0.0/Updaemon.GithubDistributionService-"));
            }
        }

        [Fact]
        public async Task GetOrInstallAsync_RegistryPluginFromTheRunningRelease_IsUsedAsInstalled()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();

                await h.Plugins.GetOrInstallAsync("github");

                Assert.Empty(h.PluginDownloader.MethodCalls);
            }
        }

        public static TheoryData<Exception> ReinstallFailures => new TheoryData<Exception>
        {
            new HttpRequestException("offline"),
            new ChecksumException("Updaemon.GithubDistributionService-linux-arm64 does not match its SHA-256 in SHA256SUMS."),
        };

        [Theory]
        [MemberData(nameof(ReinstallFailures))]
        public async Task GetOrInstallAsync_ReinstallFails_FailsNamingWhyAndKeepsTheInstalledOne(Exception failure)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.Config.AddOrUpdatePluginAsync(PluginRecords.Installed("github", "/old/plugin", OlderRelease));
                h.PluginDownloader.ExceptionToThrow = failure;

                PluginException ex = await Assert.ThrowsAsync<PluginException>(() => h.Plugins.GetOrInstallAsync("github"));

                Assert.Equal($"updating the 'github' plugin failed: {failure.Message}", ex.Message);
                InstalledPluginInfo kept = (await h.Config.GetPluginAsync("github"))!;
                Assert.Equal("/old/plugin", kept.Path);
                Assert.Equal(new PluginSource.Registry("github", OlderRelease), kept.Source);
            }
        }

        [Fact]
        public async Task GetOrInstallAsync_UrlPlugin_IsUsedAsInstalled()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.Config.AddOrUpdatePluginAsync(PluginRecords.FromUrl("mine", "/p"));

                InstalledPluginInfo plugin = await h.Plugins.GetOrInstallAsync("mine");

                Assert.Equal("/p", plugin.Path);
                Assert.Empty(h.PluginDownloader.MethodCalls);
            }
        }
    }
}
