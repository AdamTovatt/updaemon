using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class UpdateCommandTests
    {
        private static readonly string RestartCall = MockServiceManager.Call(nameof(IServiceManager.RestartServiceAsync), "byteshelf");
        private static readonly string StartCall = MockServiceManager.Call(nameof(IServiceManager.StartServiceAsync), "byteshelf");

        [Fact]
        public async Task Update_NewerRelease_ActivatesItAndRestartsTheService()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "1.1.0", AppHarness.ByteShelfManifest);
                h.ServiceManager.ServiceRunningStates["byteshelf"] = true;

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.Equal(new Version(1, 1, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.Contains(RestartCall, h.ServiceManager.MethodCalls);
                Assert.Contains("Updated byteshelf from 1.0.0 to 1.1.0.", h.Output.Messages);
            }
        }

        [Theory]
        [InlineData(false, "1.0.0")]
        [InlineData(true, "1.1.0")]
        public async Task InstallAndUpdate_DownloadWithoutAChecksum_SayItWasNotVerified(bool installVerified, string unverifiedVersion)
        {
            using (AppHarness h = new AppHarness())
            {
                h.Client.DownloadsVerified = installVerified;
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "1.1.0", AppHarness.ByteShelfManifest);
                h.Client.DownloadsVerified = !installVerified;
                await h.Update().ExecuteAsync(Array.Empty<string>());

                string[] notVerified = h.Output.Messages.Where(message => message.StartsWith("Not verified: ", StringComparison.Ordinal)).ToArray();
                Assert.Equal(new[] { $"Not verified: byteshelf {unverifiedVersion} came without a checksum to check it against." }, notVerified);
            }
        }

        [Fact]
        public async Task Update_NewVersionDoesNotStayRunning_RunsThePreviousAgainAndSkipsTheNew()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "1.1.0", AppHarness.ByteShelfManifest);
                h.ServiceManager.CrashOnNextStart.Add("byteshelf");
                h.ServiceManager.MethodCalls.Clear();

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(1, exitCode);
                Assert.Equal(
                    new[] { "Error: byteshelf: 1.1.0 did not stay running, so 1.0.0 runs again and 1.1.0 is skipped. See 'updaemon logs byteshelf'." },
                    h.Output.Errors);
                Assert.Equal(new Version(1, 0, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.Equal(new[] { new Version(1, 1, 0) }, (await h.Config.GetAppAsync("byteshelf"))!.SkippedVersions);
                // Two (re)starts: the new version, then the previous one after rolling back.
                Assert.Equal(2, h.ServiceManager.MethodCalls.Count(call => call == RestartCall || call == StartCall));
            }
        }

        [Fact]
        public async Task Update_NewVersionDoesNotStayRunningAndGoingBackFails_ReportsBoth()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "1.1.0", AppHarness.ByteShelfManifest);
                h.ServiceManager.CrashOnNextStart.Add("byteshelf");
                // The version to go back to is gone from disk.
                Directory.Delete(Path.Combine(h.Scope.GetAppDirectory("byteshelf"), "1.0.0"), recursive: true);

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(1, exitCode);
                Assert.Equal(
                    new[] { "Error: byteshelf: 1.1.0 did not stay running, and going back to 1.0.0 failed: byteshelf 1.0.0 is not kept. Kept versions: 1.1.0. See 'updaemon logs byteshelf'." },
                    h.Output.Errors);
            }
        }

        [Fact]
        public async Task Update_LatestIsSkipped_PassesOverIt()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "1.1.0", AppHarness.ByteShelfManifest);
                await h.SkipAsync("byteshelf", new Version(1, 1, 0));
                h.Client.Downloads.Clear();

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.Empty(h.Client.Downloads);
                Assert.Contains(h.Output.Messages, m => m.StartsWith("byteshelf: 1.1.0 is skipped"));
            }
        }

        [Fact]
        public async Task Update_PastAVersionThatFailed_KeepsItSkippedWhileItIsKept()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfPastACrashingReleaseAsync();

                Assert.Equal(new Version(1, 2, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.Equal(new[] { new Version(1, 1, 0) }, (await h.Config.GetAppAsync("byteshelf"))!.SkippedVersions);
            }
        }

        [Fact]
        public async Task Update_NewerThanASkippedVersionThatIsNotKept_ClearsTheSkip()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                await h.SkipAsync("byteshelf", new Version(1, 1, 0));
                h.AddRelease("owner/byteshelf", "1.2.0", AppHarness.ByteShelfManifest);

                await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(new Version(1, 2, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.Empty((await h.Config.GetAppAsync("byteshelf"))!.SkippedVersions);
            }
        }

        [Fact]
        public async Task Update_AllApps_EndsByUpdatingUpdaemon()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.Releases.Publish(UpdaemonVersion.Of(1, 1, 0));
                h.RunTheScopesOwnUpdaemon();

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.Equal("Updated updaemon from 1.0.0 to 1.1.0.", h.Output.Messages[^1]);
            }
        }

        [Fact]
        public async Task Update_NoAppsInstalled_StillUpdatesUpdaemon()
        {
            using (AppHarness h = new AppHarness())
            {
                h.Releases.Publish(UpdaemonVersion.Of(1, 1, 0));
                h.RunTheScopesOwnUpdaemon();

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.Equal("Updated updaemon from 1.0.0 to 1.1.0.", h.Output.Messages[^1]);
            }
        }

        [Fact]
        public async Task Update_AppsUpdateButUpdaemonDoesNot_Fails()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.RunTheScopesOwnUpdaemon();
                h.Releases.LatestFailure = new HttpRequestException("no network");

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(1, exitCode);
                Assert.Equal(new[] { "Error: updaemon: could not read its latest release: no network" }, h.Output.Errors);
            }
        }

        [Theory]
        [InlineData(new string[0], UpdateStarter.Person)]
        [InlineData(new[] { "--timer" }, UpdateStarter.Timer)]
        public async Task Update_EachRun_IsRecordedWithWhatStartedItAndEveryFailureItReported(string[] args, UpdateStarter startedBy)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "1.1.0", """{ "executablePath": "bin/byteshelf" }""");
                await h.Config.SaveAppAsync(new InstalledApp { Name = "orphan", Plugin = "gone", Remote = "x/y" });
                await h.Config.SaveAppAsync(new InstalledApp { Name = "stray", Plugin = "gone", Remote = "x/z" });
                await h.Config.AddOrUpdatePluginAsync(PluginRecords.FromGitHub("s3", "/plugins/s3/old", "someone", "updaemon-s3", "v1.0.0"));
                h.GitHubPlugins.NextFailures.Enqueue(new HttpRequestException("rate limited"));
                h.RunTheScopesOwnUpdaemon();
                h.Releases.LatestFailure = new HttpRequestException("no network");

                await h.Update().ExecuteAsync(args);

                UpdateRun run = (await h.RunLog.LoadAsync())!;
                Assert.Equal(h.Time.GetUtcNow(), run.FinishedAt);
                Assert.Equal(startedBy, run.StartedBy);
                const string oldManifest = "Invalid updaemon.json: it uses the old format ('executablePath'). Use 'executable' plus 'command' and/or 'service'; see PublishingWithUpdaemon.md.";
                const string missingPlugin = "the plugin 'gone' is not installed and is not in the plugin registry (github). Install it with 'updaemon dist-install --as gone <url>'.";
                Assert.Equal(
                    new[]
                    {
                        new UpdateFailure("byteshelf", oldManifest),
                        new UpdateFailure("orphan", missingPlugin),
                        new UpdateFailure("stray", missingPlugin),
                        new UpdateFailure("the 's3' plugin (github:someone/updaemon-s3)", "could not check for a newer release: rate limited. v1.0.0 stays."),
                        new UpdateFailure("updaemon", "could not read its latest release: no network"),
                    },
                    run.Failures);
                // The two apps of the missing plugin share one line.
                Assert.Equal(
                    new[]
                    {
                        $"Error: byteshelf: {oldManifest}",
                        $"Error: orphan, stray: {missingPlugin}",
                        "Error: the 's3' plugin (github:someone/updaemon-s3): could not check for a newer release: rate limited. v1.0.0 stays.",
                        "Error: updaemon: could not read its latest release: no network",
                    },
                    h.Output.Errors);
            }
        }

        /// <summary>What reading updaemon's latest release throws, and the failure the run's record keeps.</summary>
        public static TheoryData<Exception, string> UnforeseenFailures => new TheoryData<Exception, string>
        {
            // HttpClient's timeout: a cancellation the run did not ask for.
            { new TaskCanceledException("The request timed out."), "could not read its latest release: The request timed out." },
            // Anything no step reports itself still fails the run on its record.
            { new InvalidOperationException("Something unforeseen."), "Something unforeseen." },
        };

        [Theory]
        [MemberData(nameof(UnforeseenFailures))]
        public async Task Update_StepThrows_IsRecordedAsAFailure(Exception thrown, string reason)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.RunTheScopesOwnUpdaemon();
                h.Releases.LatestFailure = thrown;

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(1, exitCode);
                Assert.Equal(new[] { $"Error: updaemon: {reason}" }, h.Output.Errors);
                Assert.Equal(new UpdateFailure("updaemon", reason), Assert.Single((await h.RunLog.LoadAsync())!.Failures));
            }
        }

        /// <summary>A full run where byteshelf's 1.1.0 and updaemon's update fail.</summary>
        private static async Task<UpdateRun> FailedFullRunAsync(AppHarness h)
        {
            await h.InstallByteShelfAsync();
            h.AddRelease("owner/byteshelf", "1.1.0", """{ "executablePath": "bin/byteshelf" }""");
            h.RunTheScopesOwnUpdaemon();
            h.Releases.LatestFailure = new HttpRequestException("no network");
            Assert.Equal(1, await h.Update().ExecuteAsync(new[] { TimerRun.Option }));
            h.Output.Clear();
            return (await h.RunLog.LoadAsync())!;
        }

        [Fact]
        public async Task Update_OneAppSucceeds_ClearsOnlyItsFailureFromTheRecord()
        {
            using (AppHarness h = new AppHarness())
            {
                UpdateRun full = await FailedFullRunAsync(h);
                h.AddRelease("owner/byteshelf", "1.2.0", AppHarness.ByteShelfManifest);

                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "byteshelf" }));

                // The health check's wait moved the clock on; the record keeps the full run's time.
                Assert.NotEqual(full.FinishedAt, h.Time.GetUtcNow());
                UpdateRun run = (await h.RunLog.LoadAsync())!;
                Assert.Equal((full.FinishedAt, UpdateStarter.Timer), (run.FinishedAt, run.StartedBy));
                Assert.Equal(new[] { "updaemon" }, run.Failures.Select(failure => failure.Subject));
            }
        }

        [Fact]
        public async Task Update_OneAppFails_LeavesTheRecord()
        {
            using (AppHarness h = new AppHarness())
            {
                await FailedFullRunAsync(h);
                string record = File.ReadAllText(h.Scope.LastUpdateFilePath);

                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "byteshelf" }));

                Assert.Equal(record, File.ReadAllText(h.Scope.LastUpdateFilePath));
            }
        }

        [Fact]
        public async Task Update_RunCannotBeRecorded_WarnsAndKeepsItsOutcome()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                Directory.CreateDirectory(h.Scope.LastUpdateFilePath);

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.StartsWith("Warning: this run could not be recorded for 'updaemon status': ", Assert.Single(h.Output.Errors));
            }
        }

        [Fact]
        public async Task Update_OneApp_LeavesUpdaemonAlone()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.Releases.Publish(UpdaemonVersion.Of(1, 1, 0));
                h.RunTheScopesOwnUpdaemon();

                await h.Update().ExecuteAsync(new[] { "byteshelf" });

                Assert.DoesNotContain(h.Output.Messages, m => m.Contains("updaemon from"));
                Assert.Empty(h.Releases.Downloads);
            }
        }

        [Theory]
        [InlineData(false, "v1.1.0")]
        [InlineData(true, "v1.0.0")]
        public async Task Update_GitHubPluginWithANewerRelease_IsUpdatedUnlessOneAppIsNamed(bool oneApp, string expectedTag)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                await h.Config.AddOrUpdatePluginAsync(PluginRecords.FromGitHub("s3", "/plugins/s3/old", "someone", "updaemon-s3", "v1.0.0"));
                h.GitHubPlugins.LatestTags["someone/updaemon-s3"] = "v1.1.0";

                int exitCode = await h.Update().ExecuteAsync(oneApp ? new[] { "byteshelf" } : Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.Equal(new PluginSource.GitHub("someone", "updaemon-s3", expectedTag), (await h.Config.GetPluginAsync("s3"))!.Source);
            }
        }

        [Fact]
        public async Task Update_PluginFromAnOlderReleaseCannotBeReplaced_FailsItsAppsNamingWhy()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                await h.Config.AddOrUpdatePluginAsync(PluginRecords.Installed("github", (await h.Config.GetPluginAsync("github"))!.Path, UpdaemonVersion.Of(0, 9, 0)));
                h.PluginDownloader.ExceptionToThrow = new HttpRequestException("offline");
                h.Client.Downloads.Clear();

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(1, exitCode);
                Assert.Equal(new[] { "Error: byteshelf: updating the 'github' plugin failed: offline" }, h.Output.Errors);
                Assert.Empty(h.Client.Downloads);
            }
        }

        [Fact]
        public async Task Update_AlreadyLatest_DownloadsNothing()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.Client.Downloads.Clear();

                int exitCode = await h.Update().ExecuteAsync(new[] { "byteshelf" });

                Assert.Equal(0, exitCode);
                Assert.Empty(h.Client.Downloads);
                Assert.Contains("byteshelf is up to date (1.0.0).", h.Output.Messages);
            }
        }

        [Fact]
        public async Task Update_NewRequiredSettingWithoutValue_KeepsOldVersionAndNamesTheSetCommand()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                string manifest = AppHarness.ByteShelfManifest.Replace(
                    "\"settings\": {",
                    "\"settings\": { \"BYTESHELF_DEVICE\": { \"required\": true },");
                h.AddRelease("owner/byteshelf", "1.1.0", manifest);
                h.ServiceManager.MethodCalls.Clear();

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(1, exitCode);
                string error = Assert.Single(h.Output.Errors);
                Assert.StartsWith("Error: byteshelf: ", error);
                Assert.Contains("updaemon set byteshelf BYTESHELF_DEVICE=<value>", error);
                Assert.Equal(new Version(1, 0, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.False(Directory.Exists(Path.Combine(h.Scope.GetAppDirectory("byteshelf"), "1.1.0")));
                Assert.Empty(h.ServiceManager.MethodCalls);
            }
        }

        [Fact]
        public async Task Update_ReleaseMarksASettingSecret_MovesItsValueIntoTheSecrets()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                string manifest = AppHarness.ByteShelfManifest.Replace(
                    "\"description\": \"Server URL\", \"required\": true",
                    "\"description\": \"Server URL\", \"required\": true, \"secret\": true");
                h.AddRelease("owner/byteshelf", "1.1.0", manifest);

                await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Empty((await h.Config.GetAppAsync("byteshelf"))!.Settings);
                Assert.Equal("https://shelf", (await h.Secrets.GetAppSecretsAsync("byteshelf"))["BYTESHELF_URL"]);
                Assert.DoesNotContain("https://shelf", await File.ReadAllTextAsync(h.Scope.GetSettingsFilePath("byteshelf")));
            }
        }

        [Fact]
        public async Task Update_ManualRestart_DeploysWithoutRestarting()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                InstalledApp app = (await h.Config.GetAppAsync("byteshelf"))!;
                app.AutoRestart = false;
                await h.Config.SaveAppAsync(app);
                h.AddRelease("owner/byteshelf", "1.1.0", AppHarness.ByteShelfManifest);
                h.ServiceManager.MethodCalls.Clear();

                await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(new Version(1, 1, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.DoesNotContain(RestartCall, h.ServiceManager.MethodCalls);
                Assert.DoesNotContain(StartCall, h.ServiceManager.MethodCalls);
            }
        }

        [Fact]
        public async Task Update_ReleaseStartsShippingAManifest_DropsTheOneFromInstallFlags()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/tool", "1.0.0", manifest: null, executable: "tool");
                await h.Install().ExecuteAsync(new[] { "tool", "--from", "github:owner/tool", "--command", "tool" });
                h.AddRelease("owner/tool", "1.1.0", """{ "executable": "tool", "command": "tool" }""", executable: "tool");

                await h.Update().ExecuteAsync(new[] { "tool" });

                Assert.Null((await h.Config.GetAppAsync("tool"))!.Manifest);
                Assert.Equal(new Version(1, 1, 0), await h.Deployer.GetCurrentVersionAsync("tool"));
            }
        }

        [Fact]
        public async Task Update_Failures_PrintOneLineNamingTheApp()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "1.1.0", """{ "executablePath": "bin/byteshelf" }""");
                InstalledApp orphan = new InstalledApp { Name = "orphan", Plugin = "gone", Remote = "x/y" };
                await h.Config.SaveAppAsync(orphan);

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.StartsWith("Error: byteshelf: Invalid updaemon.json") && !e.Contains('\n'));
                Assert.Contains(h.Output.Errors, e => e.StartsWith("Error: orphan: the plugin 'gone' is not installed"));
                Assert.Equal(new Version(1, 0, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
            }
        }

        [Fact]
        public async Task Update_PluginFailsToStart_NamesItsApps()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                InstalledPluginInfo plugin = (await h.Config.GetPluginAsync("github"))!;
                h.Client.ConnectAsyncThrowsForPluginPaths.Add(plugin.Path);

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.StartsWith("Error: byteshelf: the plugin 'github' failed to start"));
            }
        }

        [Fact]
        public async Task Update_NoReleasedVersion_SaysSo()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.Client.LatestVersions["owner/byteshelf"] = null;

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.Contains("byteshelf: no released version found for github:owner/byteshelf.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task Update_RestartFails_ReportsItForTheApp()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "1.1.0", AppHarness.ByteShelfManifest);
                h.ServiceManager.ServiceRunningStates["byteshelf"] = true;
                h.ServiceManager.RestartFailure = new InvalidOperationException("Simulated restart failure");

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(1, exitCode);
                Assert.Contains("Error: byteshelf: Simulated restart failure", h.Output.Errors);
            }
        }

        [Theory]
        [InlineData("Unknown option '--foo'", "--foo")]
        [InlineData("Usage: updaemon update", "a", "b")]
        [InlineData("'missing' is not installed", "missing")]
        public async Task Update_InvalidArguments_Fail(string expectedError, params string[] args)
        {
            using (AppHarness h = new AppHarness())
            {
                int exitCode = await h.Update().ExecuteAsync(args);

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains(expectedError));
            }
        }
    }
}
