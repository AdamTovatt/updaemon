using Updaemon.Common.Utilities;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    /// <summary>A GitHub-source plugin follows its repository's latest release while self-update is on.</summary>
    public class GitHubPluginUpdaterTests
    {
        private static async Task<AppHarness> WithPluginAtAsync(string tag)
        {
            AppHarness h = new AppHarness();
            await h.Config.AddOrUpdatePluginAsync(PluginRecords.FromGitHub("s3", "/plugins/s3/old", "someone", "updaemon-s3", tag));
            return h;
        }

        private static async Task<PluginSource> SourceOfAsync(AppHarness h, string alias = "s3") => (await h.Config.GetPluginAsync(alias))!.Source;

        [Fact]
        public async Task RunAsync_NewerRelease_IsInstalledVerifiedAndRecorded()
        {
            using (AppHarness h = await WithPluginAtAsync("v1.0.0"))
            {
                h.GitHubPlugins.LatestTags["someone/updaemon-s3"] = "v1.1.0";

                Assert.True(await h.GitHubPluginUpdater().RunAsync());

                Assert.Equal((MockGitHubPluginReleases.BuildUrl("someone", "updaemon-s3", "v1.1.0"), true), Assert.Single(h.PluginDownloader.Downloads));
                Assert.Equal(new PluginSource.GitHub("someone", "updaemon-s3", "v1.1.0"), await SourceOfAsync(h));
                Assert.Contains("Updated the 's3' plugin from v1.0.0 to v1.1.0.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_InstalledReleaseIsTheLatest_LeavesIt()
        {
            using (AppHarness h = await WithPluginAtAsync("v1.0.0"))
            {
                h.GitHubPlugins.LatestTags["someone/updaemon-s3"] = "v1.0.0";

                Assert.True(await h.GitHubPluginUpdater().RunAsync());

                Assert.Empty(h.PluginDownloader.Downloads);
                Assert.Equal(new[] { "The 's3' plugin is up to date (v1.0.0)." }, h.Output.Messages);
            }
        }

        public static TheoryData<Exception> Refusals => new TheoryData<Exception>
        {
            new ChecksumException("s3plugin-linux-arm64 does not match its SHA-256 in SHA256SUMS; the download is corrupt or was tampered with."),
            new ReleaseBuildException("No asset in the release v2.0.0 of someone/updaemon-s3 is named *-linux-arm64, the plugin build for this machine."),
            new PluginProtocolException("The plugin at /tmp/x speaks plugin protocol 3, but this updaemon speaks protocol 2."),
        };

        [Theory]
        [MemberData(nameof(Refusals))]
        public async Task RunAsync_NewerReleaseIsRefused_KeepsTheInstalledOneAndSkipsTheRelease(Exception refusal)
        {
            using (AppHarness h = await WithPluginAtAsync("v1.0.0"))
            {
                h.GitHubPlugins.LatestTags["someone/updaemon-s3"] = "v2.0.0";
                h.PluginDownloader.ExceptionToThrow = refusal;

                Assert.False(await h.GitHubPluginUpdater().RunAsync());

                InstalledPluginInfo kept = (await h.Config.GetPluginAsync("s3"))!;
                Assert.Equal("/plugins/s3/old", kept.Path);
                Assert.Equal(new PluginSource.GitHub("someone", "updaemon-s3", "v1.0.0") { SkippedTag = "v2.0.0" }, kept.Source);
                Assert.Equal(
                    $"Error: the 's3' plugin (github:someone/updaemon-s3) could not update to v2.0.0: {refusal.Message.TrimEnd('.')}. " +
                    "v1.0.0 stays. Later updates skip v2.0.0 until another release is latest.",
                    Assert.Single(h.Output.Errors));
            }
        }

        [Fact]
        public async Task RunAsync_SkippedReleaseIsStillTheLatest_PassesOverItWithoutFailing()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.Config.AddOrUpdatePluginAsync(new InstalledPluginInfo
                {
                    Alias = "s3",
                    Path = "/plugins/s3/old",
                    Source = new PluginSource.GitHub("someone", "updaemon-s3", "v1.0.0") { SkippedTag = "v2.0.0" },
                });
                h.GitHubPlugins.LatestTags["someone/updaemon-s3"] = "v2.0.0";

                Assert.True(await h.GitHubPluginUpdater().RunAsync());

                Assert.Empty(h.PluginDownloader.Downloads);
                Assert.Equal(new[] { "The 's3' plugin skips v2.0.0, which was refused; v1.0.0 stays." }, h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_ReleaseAfterASkippedOne_IsInstalledAndClearsTheSkip()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.Config.AddOrUpdatePluginAsync(new InstalledPluginInfo
                {
                    Alias = "s3",
                    Path = "/plugins/s3/old",
                    Source = new PluginSource.GitHub("someone", "updaemon-s3", "v1.0.0") { SkippedTag = "v2.0.0" },
                });
                h.GitHubPlugins.LatestTags["someone/updaemon-s3"] = "v2.0.1";

                Assert.True(await h.GitHubPluginUpdater().RunAsync());

                Assert.Equal(new PluginSource.GitHub("someone", "updaemon-s3", "v2.0.1"), await SourceOfAsync(h));
            }
        }

        public static TheoryData<Exception> PassingFailures => new TheoryData<Exception>
        {
            new HttpRequestException("offline"),
            new IOException("disk full"),
            new PluginException("The plugin process exited before establishing pipe connection."),
        };

        [Theory]
        [MemberData(nameof(PassingFailures))]
        public async Task RunAsync_InstallFailsForAnotherReason_IsReportedNotSkippedAndTheOthersStillRun(Exception failure)
        {
            using (AppHarness h = await WithPluginAtAsync("v1.0.0"))
            {
                await h.Config.AddOrUpdatePluginAsync(PluginRecords.FromGitHub("other", "/plugins/other/old", "someone", "other", "v1"));
                h.GitHubPlugins.LatestTags["someone/updaemon-s3"] = "v2.0.0";
                h.GitHubPlugins.LatestTags["someone/other"] = "v1";
                h.PluginDownloader.ExceptionToThrow = failure;

                Assert.False(await h.GitHubPluginUpdater().RunAsync());

                Assert.Equal(new PluginSource.GitHub("someone", "updaemon-s3", "v1.0.0"), await SourceOfAsync(h));
                Assert.Equal(
                    $"Error: the 's3' plugin (github:someone/updaemon-s3) could not update to v2.0.0: {failure.Message.TrimEnd('.')}. v1.0.0 stays.",
                    Assert.Single(h.Output.Errors));
                Assert.Contains("The 'other' plugin is up to date (v1).", h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_LatestReleaseCannotBeRead_ReportsItAndFails()
        {
            using (AppHarness h = await WithPluginAtAsync("v1.0.0"))
            {
                h.GitHubPlugins.Failure = new HttpRequestException("offline");

                Assert.False(await h.GitHubPluginUpdater().RunAsync());

                Assert.Equal("Error: the 's3' plugin (github:someone/updaemon-s3) could not check for a newer release: offline. v1.0.0 stays.", Assert.Single(h.Output.Errors));
            }
        }

        [Fact]
        public async Task RunAsync_SelfUpdateOff_ChecksNothing()
        {
            using (AppHarness h = await WithPluginAtAsync("v1.0.0"))
            {
                UpdaemonConfig config = await h.Config.LoadConfigAsync();
                config.SelfUpdate = false;
                await h.Config.SaveConfigAsync(config);
                h.GitHubPlugins.Failure = new HttpRequestException("must not be asked");

                Assert.True(await h.GitHubPluginUpdater().RunAsync());

                Assert.Empty(h.Output.Messages);
                Assert.Empty(h.Output.Errors);
            }
        }

        [Fact]
        public async Task RunAsync_RegistryAndUrlPlugins_AreLeftAlone()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                await h.Config.AddOrUpdatePluginAsync(PluginRecords.FromUrl("mine", "/p"));
                h.GitHubPlugins.Failure = new HttpRequestException("must not be asked");

                Assert.True(await h.GitHubPluginUpdater().RunAsync());

                Assert.Empty(h.PluginDownloader.Downloads);
                Assert.Empty(h.Output.Errors);
            }
        }
    }
}
