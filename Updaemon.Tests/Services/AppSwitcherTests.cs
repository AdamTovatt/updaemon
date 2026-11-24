using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;
using static Updaemon.Tests.Helpers.ShelfPackages;

namespace Updaemon.Tests.Services
{
    /// <summary>What a switch leaves when its run is killed, and how the next change to the app puts it right.</summary>
    public class AppSwitcherTests
    {
        /// <summary>A release whose every generated file differs from 1.0.0's.</summary>
        private static readonly string ChangedManifest = AppHarness.HostManifest
            .Replace("\"command\": \"shelf\"", "\"command\": \"shelf2\"")
            .Replace("\"serve\"", "\"serve2\"")
            .Replace("SHELF_PLUGINS", "SHELF_EXTENSIONS");

        public static TheoryData<string, Action<AppHarness>> PointsToBeKilledAt => new TheoryData<string, Action<AppHarness>>
        {
            { "linked, no file written", h => h.Permissions.OnMakeTreeReadable = path =>
                {
                    if (path == h.Scope.GetExtensionsDirectory("shelf"))
                    {
                        h.KeepForCutOff();
                    }
                }
            },
            { "files written", h => h.ServiceManager.OnDaemonReload = h.KeepForCutOff },
            { "restarted", h => h.ServiceManager.OnRestart = h.KeepForCutOff },
        };

        /// <summary>An update of the host from 1.0.0 to 2.0.0 killed at <paramref name="point"/>, then the network gone.</summary>
        private static async Task<(AppHarness Harness, IReadOnlyList<string> Before)> KilledUpdateAsync(Action<AppHarness> point)
        {
            AppHarness h = new AppHarness();
            await h.InstallHostWithImagesAsync();
            IReadOnlyList<string> before = h.Snapshot();
            h.AddHostRelease("2.0.0", ChangedManifest);
            point(h);
            Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));
            h.CutOff();
            Assert.NotNull((await h.HostAsync()).Unfinished);
            h.Client.RefusedRemotes["owner/shelf"] = "no network";
            h.Client.RefusedRemotes["owner/shelf/images.zip"] = "no network";
            return (h, before);
        }

        private static IReadOnlyList<string> WithoutVersion(AppHarness h, IReadOnlyList<string> snapshot, string version) =>
            snapshot.Where(entry => !entry.StartsWith(Path.Combine(h.Scope.GetAppDirectory("shelf"), version), StringComparison.Ordinal)).ToList();

        public static TheoryData<string, Func<AppHarness, Task<int>>> CommandsThatPutRight => new TheoryData<string, Func<AppHarness, Task<int>>>
        {
            { "update", h => h.Update().ExecuteAsync(new[] { "shelf" }) },
            { "set", h => h.Set().ExecuteAsync(new[] { "shelf", "A-B=1" }) },
            { "unset", h => h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }) },
            { "set-args", h => h.SetArgs().ExecuteAsync(new[] { "shelf" }) },
            { "rollback", h => h.RollbackCommand().ExecuteAsync(new[] { "shelf", "1.0.0" }) },
            { "install", h => h.Install().ExecuteAsync(new[] { "shelf/audio", "--from", "github:owner/shelf" }) },
        };

        [Theory]
        [MemberData(nameof(PointsToBeKilledAt))]
        public async Task KilledUpdate_NextUpdate_PutsEverythingBackWithoutTheNetworkAndSkipsNothing(string name, Action<AppHarness> point)
        {
            _ = name; // Names the case in the test's display name.
            (AppHarness h, IReadOnlyList<string> before) = await KilledUpdateAsync(point);
            using (h)
            {
                await h.Update().ExecuteAsync(new[] { "shelf" });

                Assert.Contains("shelf: an earlier run was cut off while it switched versions; putting back shelf 1.0.0.", h.Output.Messages);
                Assert.Equal(new[] { "Error: shelf: no network", "Error: shelf/images: no network" }, h.Output.Errors);
                // The service may run either version: it is restarted on the one put back.
                Assert.Equal(1, h.HostRestarts());
                Assert.Equal(WithoutVersion(h, before, "2.0.0"), WithoutVersion(h, h.Snapshot(), "2.0.0"));
            }
        }

        [Theory]
        [MemberData(nameof(CommandsThatPutRight))]
        public async Task KilledUpdate_AnyChangeToTheApp_PutsItBackFirst(string name, Func<AppHarness, Task<int>> run)
        {
            _ = name; // Names the case in the test's display name.
            (AppHarness h, _) = await KilledUpdateAsync(harness => harness.ServiceManager.OnRestart = harness.KeepForCutOff);
            using (h)
            {
                await run(h);

                Assert.Equal(One, await h.Versions.GetCurrentAsync(new AppName("shelf")));
                InstalledApp host = await h.HostAsync();
                Assert.Null(host.Unfinished);
                Assert.Empty(host.SkippedVersions);
                Assert.True(File.Exists(h.Scope.GetCommandPath("shelf")));
                Assert.False(File.Exists(h.Scope.GetCommandPath("shelf2")));
            }
        }

        [Fact]
        public async Task KilledUpdate_NextUpdateWithTheNetwork_UpdatesAgain()
        {
            (AppHarness h, _) = await KilledUpdateAsync(harness => harness.ServiceManager.OnRestart = harness.KeepForCutOff);
            using (h)
            {
                h.Client.RefusedRemotes.Clear();

                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal(Two, await h.Versions.GetCurrentAsync(new AppName("shelf")));
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        [Theory]
        [MemberData(nameof(PointsToBeKilledAt))]
        public async Task KilledUpdate_RemovingTheApp_LeavesNothingOfEitherVersion(string name, Action<AppHarness> point)
        {
            _ = name; // Names the case in the test's display name.
            (AppHarness h, _) = await KilledUpdateAsync(point);
            using (h)
            {
                Assert.Equal(0, await h.Remove().ExecuteAsync(new[] { "shelf" }));

                Assert.Empty(Directory.GetFileSystemEntries(h.Scope.BinDirectory));
                Assert.False(Directory.Exists(h.Scope.GetAppDirectory("shelf")));
            }
        }

        /// <summary>The way out when the version to put back is gone: removing the app still works.</summary>
        [Fact]
        public async Task KilledUpdate_VersionToPutBackIsGone_OnlyRemoveGoesOn()
        {
            (AppHarness h, _) = await KilledUpdateAsync(harness => harness.ServiceManager.OnRestart = harness.KeepForCutOff);
            using (h)
            {
                Directory.Delete(h.Versions.GetVersionDirectory(new AppName("shelf"), One), recursive: true);

                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));
                Assert.Equal(
                    "Error: shelf: putting back shelf 1.0.0 failed: shelf 1.0.0 is not kept. Kept versions: 2.0.0. Until that works shelf cannot be changed; 'updaemon remove shelf' still removes it.",
                    Assert.Single(h.Output.Errors));
                Assert.NotNull((await h.HostAsync()).Unfinished);

                Assert.Equal(0, await h.Remove().ExecuteAsync(new[] { "shelf" }));
                Assert.Null(await h.Config.GetAppAsync("shelf"));
            }
        }

        [Fact]
        public async Task KilledUpdate_RestartOfWhatIsPutBackFails_WarnsAndGoesOn()
        {
            (AppHarness h, _) = await KilledUpdateAsync(harness => harness.ServiceManager.OnRestart = harness.KeepForCutOff);
            using (h)
            {
                h.ServiceManager.RestartFailure = new InvalidOperationException("no restart today");

                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                Assert.Equal(new[] { "Warning: restarting shelf failed: no restart today" }, h.Output.Errors);
                Assert.Equal(One, await h.Versions.GetCurrentAsync(new AppName("shelf")));
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        [Theory]
        [MemberData(nameof(PointsToBeKilledAt))]
        public async Task KilledInstallOfAnExtension_NextChange_TakesItOutAgain(string name, Action<AppHarness> point)
        {
            _ = name; // Names the case in the test's display name.
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                IReadOnlyList<string> before = h.Snapshot();
                point(h);
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/images" }));
                h.CutOff();

                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                Assert.Contains("shelf: an earlier run was cut off while it switched versions; putting back no shelf/images.", h.Output.Messages);
                Assert.Equal(1, h.HostRestarts());
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task KilledInstallOfAnExtension_InstallingItAgain_Works()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                await h.KilledInstallOfImagesAsync();

                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(new[] { "images -> 1.0.0" }, h.LinkedExtensions());
            }
        }

        /// <summary>Unlinked, but not recorded: it is linked again, and the host, which may have started without it since, restarted.</summary>
        [Fact]
        public async Task KilledRemovalOfAnExtension_BeforeItIsRecorded_IsPutBack()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                IReadOnlyList<string> before = h.Snapshot();
                h.SwitcherConfig.OnSave = app =>
                {
                    if (app.Extensions.Count == 0)
                    {
                        h.KeepForCutOff();
                    }
                };
                Assert.Equal(0, await h.Remove().ExecuteAsync(new[] { "shelf/images" }));
                h.CutOff();
                Assert.Empty(h.LinkedExtensions());

                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                Assert.Contains("shelf: an earlier run was cut off while it switched versions; putting back shelf/images 1.0.0.", h.Output.Messages);
                Assert.Equal(1, h.HostRestarts());
                h.AssertUnchangedSince(before);
            }
        }

        /// <summary>Once recorded, a removal stands: only the restart it was cut off before is made up for.</summary>
        [Fact]
        public async Task KilledRemovalOfAnExtension_OnceRecorded_OnlyTheRestartIsMadeUpFor()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                h.ServiceManager.OnRestart = h.KeepForCutOff;
                Assert.Equal(0, await h.Remove().ExecuteAsync(new[] { "shelf/images" }));
                h.CutOff();

                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                Assert.Contains("shelf: an earlier run was cut off before it restarted the service; restarting it.", h.Output.Messages);
                Assert.Equal(1, h.HostRestarts());
                Assert.Empty(h.LinkedExtensions());
                InstalledApp host = await h.HostAsync();
                Assert.Empty(host.Extensions);
                Assert.Null(host.Unfinished);
            }
        }

        [Fact]
        public async Task KilledRollback_OnceRecorded_OnlyTheRestartIsMadeUpFor()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                h.AddHostRelease("2.0.0");
                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));
                h.ServiceManager.OnRestart = h.KeepForCutOff;
                Assert.Equal(0, await h.RollbackCommand().ExecuteAsync(new[] { "shelf" }));
                h.CutOff();

                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                Assert.Equal(One, await h.Versions.GetCurrentAsync(new AppName("shelf")));
                Assert.Equal(new[] { Two }, (await h.HostAsync()).SkippedVersions);
                Assert.Equal(1, h.HostRestarts());
            }
        }

        /// <summary>The service never saw the new version, so it is left running as it was.</summary>
        [Fact]
        public async Task Update_ActivatingFails_PutsBackWithoutRestarting()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                IReadOnlyList<string> before = h.Snapshot();
                h.AddHostRelease("2.0.0", ChangedManifest);
                File.WriteAllText(h.Scope.GetCommandPath("shelf2"), "someone else's");

                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal(0, h.HostRestarts());
                File.Delete(h.Scope.GetCommandPath("shelf2"));
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Update_CancelledDuringTheHealthCheck_PutsTheVersionBackAndSkipsNothing()
        {
            using (AppHarness h = new AppHarness())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                await h.InstallHostWithImagesAsync();
                IReadOnlyList<string> before = h.Snapshot();
                h.AddHostRelease("2.0.0", ChangedManifest);
                h.ServiceManager.OnRestart = cancellation.Cancel;

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Update().ExecuteAsync(new[] { "shelf" }, cancellation.Token));

                Assert.Equal(2, h.HostRestarts());
                h.AssertUnchangedSince(before);
            }
        }

        /// <summary>1.0.0's service, in 2.0.0 gone. Activating that removes the service, so putting back has to bring it back as it was.</summary>
        private static async Task<AppHarness> WithAReleaseWithoutTheServiceAsync()
        {
            AppHarness h = new AppHarness();
            await h.InstallHostWithImagesAsync();
            h.AddHostRelease("2.0.0", AppHarness.HostManifest.Replace("\"service\": { \"args\": [\"serve\"] },", string.Empty));
            return h;
        }

        [Fact]
        public async Task Update_ToAVersionWithoutTheServiceCannotBeRecorded_EnablesAndStartsTheServiceAgain()
        {
            using (AppHarness h = await WithAReleaseWithoutTheServiceAsync())
            {
                h.SwitcherConfig.NextSaves.Enqueue(new IOException("disk full"));

                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal(new[] { "Error: shelf: disk full" }, h.Output.Errors);
                Assert.Equal(One, await h.Versions.GetCurrentAsync(new AppName("shelf")));
                Assert.True(h.ServiceManager.ServiceRunningStates["shelf"]);
                Assert.Contains("shelf", h.ServiceManager.EnabledServices);
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        [Fact]
        public async Task Update_ToAVersionWithoutTheServiceCannotBeRecorded_LeavesADisabledStoppedServiceSo()
        {
            using (AppHarness h = await WithAReleaseWithoutTheServiceAsync())
            {
                await h.ServiceManager.DisableServiceAsync("shelf");
                await h.ServiceManager.StopServiceAsync("shelf");
                h.SwitcherConfig.NextSaves.Enqueue(new IOException("disk full"));

                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.True(File.Exists(h.Scope.GetUnitFilePath("shelf")));
                Assert.False(h.ServiceManager.ServiceRunningStates["shelf"]);
                Assert.DoesNotContain("shelf", h.ServiceManager.EnabledServices);
            }
        }

        [Fact]
        public async Task Update_PuttingBackARemovedServiceFails_TheNextChangeStartsIt()
        {
            using (AppHarness h = await WithAReleaseWithoutTheServiceAsync())
            {
                h.SwitcherConfig.NextSaves.Enqueue(new IOException("disk full"));
                h.ServiceManager.StartFailure = new InvalidOperationException("no start today");

                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal(new[] { "Error: shelf: disk full, and switching back failed: no start today." }, h.Output.Errors);
                Assert.False(h.ServiceManager.ServiceRunningStates["shelf"]);
                Assert.NotNull((await h.HostAsync()).Unfinished);

                h.ServiceManager.StartFailure = null;
                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));
                Assert.True(h.ServiceManager.ServiceRunningStates["shelf"]);
                Assert.Contains("shelf", h.ServiceManager.EnabledServices);
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        /// <summary>On macOS, starting what already runs would restart it.</summary>
        [Fact]
        public async Task Update_RemovedServiceIsPutBackButThatIsNotRecorded_TheNextChangeLeavesItRunning()
        {
            using (AppHarness h = await WithAReleaseWithoutTheServiceAsync())
            {
                h.SwitcherConfig.NextSaves.Enqueue(new IOException("disk full"));
                h.SwitcherConfig.NextSaves.Enqueue(new IOException("disk full"));
                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));
                Assert.NotNull((await h.HostAsync()).Unfinished);
                Assert.True(h.ServiceManager.ServiceRunningStates["shelf"]);
                h.ServiceManager.MethodCalls.Clear();

                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                Assert.DoesNotContain(MockServiceManager.Call(nameof(IServiceManager.StartServiceAsync), "shelf"), h.ServiceManager.MethodCalls);
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        /// <summary>Killed once the service is gone: putting it back needs the service manager, though no service is there now.</summary>
        [Fact]
        public async Task KilledUpdate_ThatRemovedTheService_ChecksTheServiceManagerBeforeItPutsBack()
        {
            using (AppHarness h = await WithAReleaseWithoutTheServiceAsync())
            {
                h.ServiceManager.OnDaemonReload = h.KeepForCutOff;
                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));
                h.CutOff();
                Assert.False(File.Exists(h.Scope.GetUnitFilePath("shelf")));
                h.ServiceManager.Unreachable = "unreachable";
                IReadOnlyList<string> before = h.Snapshot();

                await Assert.ThrowsAsync<ServiceManagerUnreachableException>(() => h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));
                h.AssertUnchangedSince(before);

                h.ServiceManager.Unreachable = null;
                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));
                Assert.True(h.ServiceManager.ServiceRunningStates["shelf"]);
                Assert.Contains("shelf", h.ServiceManager.EnabledServices);
            }
        }

        /// <summary>1.0.0 is a command only; 2.0.0 adds the service. Restarting a service without a unit fails, as systemctl's does.</summary>
        private static async Task<AppHarness> WithAReleaseAddingTheServiceAsync()
        {
            AppHarness h = new AppHarness();
            await h.InstallHostWithImagesAsync(AppHarness.HostManifest.Replace("\"service\": { \"args\": [\"serve\"] },", string.Empty));
            h.AddHostRelease("2.0.0");
            h.ServiceManager.OnRestart = () => RequireUnit(h);
            return h;
        }

        private static void RequireUnit(AppHarness h)
        {
            if (!File.Exists(h.Scope.GetUnitFilePath("shelf")))
            {
                throw new InvalidOperationException("Unit shelf.service not found.");
            }
        }

        [Fact]
        public async Task Update_DoesNotStayUpAndThePreviousIsNoService_FallsBackWithoutARestart()
        {
            using (AppHarness h = await WithAReleaseAddingTheServiceAsync())
            {
                h.ServiceManager.CrashOnNextStart.Add("shelf");

                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal(new[] { "Error: shelf: 2.0.0 did not stay running, so 1.0.0 is current again and 2.0.0 is skipped. See 'updaemon logs shelf'." }, h.Output.Errors);
                Assert.Equal(1, h.HostRestarts());
                InstalledApp host = await h.HostAsync();
                Assert.Null(host.Unfinished);
                Assert.Equal(new[] { Two }, host.SkippedVersions);
            }
        }

        [Fact]
        public async Task Update_CancelledAndThePreviousIsNoService_PutsBackWithoutARestart()
        {
            using (AppHarness h = await WithAReleaseAddingTheServiceAsync())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                h.ServiceManager.OnRestart = () =>
                {
                    RequireUnit(h);
                    cancellation.Cancel();
                };

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Update().ExecuteAsync(new[] { "shelf" }, cancellation.Token));

                Assert.Empty(h.Output.Errors);
                Assert.Equal(1, h.HostRestarts());
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        [Fact]
        public async Task Update_CancelledAndCannotBePutBack_Warns()
        {
            using (AppHarness h = new AppHarness())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                await h.InstallHostWithImagesAsync();
                h.AddHostRelease("2.0.0");
                h.ServiceManager.OnRestart = () =>
                {
                    Directory.Delete(h.Versions.GetVersionDirectory(new AppName("shelf"), One), recursive: true);
                    cancellation.Cancel();
                };

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Update().ExecuteAsync(new[] { "shelf" }, cancellation.Token));

                Assert.Equal(new[] { "Warning: switching shelf back failed: shelf 1.0.0 is not kept. Kept versions: 2.0.0." }, h.Output.Errors);
            }
        }

        [Fact]
        public async Task Update_CancelledDuringTheHealthCheckOfWhatItFellBackTo_LeavesNothingToMakeUpFor()
        {
            using (AppHarness h = new AppHarness())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                await h.InstallHostWithImagesAsync();
                h.AddHostRelease("2.0.0");
                h.ServiceManager.CrashOnNextStart.Add("shelf");
                h.ServiceManager.OnCrash = () => h.ServiceManager.OnRestart = cancellation.Cancel;

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Update().ExecuteAsync(new[] { "shelf" }, cancellation.Token));

                Assert.Equal(One, await h.Versions.GetCurrentAsync(new AppName("shelf")));
                InstalledApp host = await h.HostAsync();
                Assert.Null(host.Unfinished);
                Assert.Equal(new[] { Two }, host.SkippedVersions);
            }
        }

        /// <summary>The host and its extension images, both at 1.0.0, with 2.0.0 of each downloaded and a switch to them.</summary>
        private static async Task<AppSwitch> SwitchOfBothAsync(AppHarness h)
        {
            await h.InstallHostWithImagesAsync();
            h.AddHostRelease("2.0.0", ChangedManifest);
            h.AddExtensionRelease("owner/shelf/images.zip", "2.0.0");
            PreparedApp prepared = await h.Switcher.PrepareAsync(await h.HostAsync());
            InstalledExtension images = prepared.App.FindExtension("images")!;
            DeployableVersion staged = await h.Deployer.StageAsync(prepared.App, Two, h.Client);
            await h.Versions.DownloadAsync(Images, images, Two, h.Client);
            return AppSwitch.OfAll(prepared, staged, new[] { new ExtensionSwitch(images, Two) })!;
        }

        private static async Task<(Version Host, string Images)> CurrentAsync(AppHarness h) =>
            (await h.Versions.GetCurrentAsync(new AppName("shelf")), Assert.Single(h.LinkedExtensions()));

        [Fact]
        public async Task UpdateAsync_HostAndExtension_SwitchesBothWithOneRestart()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                string imagesAtRestart = string.Empty;
                h.ServiceManager.OnRestart = () => imagesAtRestart = Assert.Single(h.LinkedExtensions());

                await h.Switcher.UpdateAsync(both);

                Assert.Equal("images -> 2.0.0", imagesAtRestart);
                Assert.Equal((Two, "images -> 2.0.0"), await CurrentAsync(h));
                Assert.Equal(1, h.HostRestarts());
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        [Fact]
        public async Task UpdateAsync_HostAndExtensionDoNotStayUp_PutsBothBackAndSkipsBoth()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                h.ServiceManager.CrashOnNextStart.Add("shelf");

                NotRunningException ex = await Assert.ThrowsAsync<NotRunningException>(() => h.Switcher.UpdateAsync(both));

                Assert.Equal(
                    "shelf did not stay running with shelf 2.0.0, shelf/images 2.0.0, so shelf 1.0.0, shelf/images 1.0.0 run again and each of the new versions is skipped. See 'updaemon logs shelf'.",
                    ex.Message);
                Assert.Equal((One, "images -> 1.0.0"), await CurrentAsync(h));
                InstalledApp host = await h.HostAsync();
                Assert.Equal(new[] { Two }, host.SkippedVersions);
                Assert.Equal(new[] { Two }, host.FindExtension("images")!.SkippedVersions);
                Assert.Null(host.Unfinished);
                Assert.Equal(2, h.HostRestarts());
            }
        }

        [Fact]
        public async Task UpdateAsync_NeitherStaysUp_SaysWhatIsCurrent()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                h.ServiceManager.CrashOnNextStart.Add("shelf");
                h.ServiceManager.OnCrash = () => h.ServiceManager.CrashOnNextStart.Add("shelf");

                NotRunningException ex = await Assert.ThrowsAsync<NotRunningException>(() => h.Switcher.UpdateAsync(both));

                Assert.Equal(
                    "shelf did not stay running with shelf 2.0.0, shelf/images 2.0.0; shelf 1.0.0, shelf/images 1.0.0 are current again but did not stay running either, and each of the new versions is skipped. See 'updaemon logs shelf'.",
                    ex.Message);
            }
        }

        [Fact]
        public async Task UpdateAsync_InstalledExtensionGoneAgainAndStillNotUp_SaysSo()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.ServiceManager.CrashOnNextStart.Add("shelf");
                h.ServiceManager.OnCrash = () => h.ServiceManager.CrashOnNextStart.Add("shelf");

                Assert.Equal(1, await h.Install().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(
                    new[] { "Error: installing shelf/images failed: shelf did not stay running with 1.0.0; it is removed again, but shelf did not stay running without it either. See 'updaemon logs shelf'." },
                    h.Output.Errors);
            }
        }

        /// <summary>The host's version cannot be put back; the extension's still is, and the rest is left to the next change.</summary>
        [Fact]
        public async Task UpdateAsync_OneOfTwoCannotBePutBack_PutsTheOtherBack()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                h.ServiceManager.OnRestart = () => Directory.Delete(h.Versions.GetVersionDirectory(new AppName("shelf"), One), recursive: true);
                h.SwitcherConfig.NextSaves.Enqueue(new IOException("disk full"));

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Switcher.UpdateAsync(both));

                Assert.Equal("disk full, and switching back failed: shelf 1.0.0 is not kept. Kept versions: 2.0.0.", ex.Message);
                Assert.Equal((Two, "images -> 1.0.0"), await CurrentAsync(h));
                Assert.NotNull((await h.HostAsync()).Unfinished);
            }
        }

        [Fact]
        public async Task UpdateAsync_ExtensionAlone_DoesNotStayUp_SkipsItsVersion()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                h.ServiceManager.CrashOnNextStart.Add("shelf");

                NotRunningException ex = await Assert.ThrowsAsync<NotRunningException>(() => h.Switcher.UpdateAsync(AppSwitch.OfAll(both.Prepared, null, both.Extensions)!));

                Assert.Equal("shelf did not stay running with 2.0.0, so 1.0.0 runs again and 2.0.0 is skipped. See 'updaemon logs shelf'.", ex.Message);
                Assert.Equal((One, "images -> 1.0.0"), await CurrentAsync(h));
                InstalledApp host = await h.HostAsync();
                Assert.Empty(host.SkippedVersions);
                Assert.Equal(new[] { Two }, host.FindExtension("images")!.SkippedVersions);
            }
        }

        /// <summary>After a failure the entry in hand is not what is saved: saving it would bring back what was put back.</summary>
        [Fact]
        public async Task UpdateAsync_AgainWithTheSameProof_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                h.SwitcherConfig.NextSaves.Enqueue(new IOException("disk full"));
                await Assert.ThrowsAsync<IOException>(() => h.Switcher.UpdateAsync(both));
                IReadOnlyList<string> before = h.Snapshot();

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Switcher.UpdateAsync(both));
                await Assert.ThrowsAsync<InvalidOperationException>(() => h.Switcher.RemoveExtensionAsync(both.Prepared, both.Extensions[0].Extension));
                await Assert.ThrowsAsync<InvalidOperationException>(() => h.Deployer.ApplySettingsAsync(both.Prepared, h.Reachable));

                Assert.Equal("shelf was changed since it was prepared; prepare it again.", ex.Message);
                h.AssertUnchangedSince(before);
                Assert.Equal((One, "images -> 1.0.0"), await CurrentAsync(h));
            }
        }

        [Fact]
        public async Task UpdateAsync_AgainWithTheSameProofAfterOneThatStands_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                await h.Switcher.UpdateAsync(both);
                IReadOnlyList<string> before = h.Snapshot();

                await Assert.ThrowsAsync<InvalidOperationException>(() => h.Switcher.UpdateAsync(AppSwitch.OfExtension(both.Prepared, both.Extensions[0].Extension, One)));

                h.AssertUnchangedSince(before);
                Assert.Equal((Two, "images -> 2.0.0"), await CurrentAsync(h));
            }
        }

        /// <summary>What to put back is only saved once: a second switch would overwrite it with what the first one left.</summary>
        [Fact]
        public async Task UpdateAsync_AnotherProofOfAnAppWhoseSwitchCouldNotBePutBack_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                PreparedApp another = await h.Switcher.PrepareAsync(both.App);
                h.ServiceManager.OnRestart = () => Directory.Delete(h.Versions.GetVersionDirectory(new AppName("shelf"), One), recursive: true);
                h.SwitcherConfig.NextSaves.Enqueue(new IOException("disk full"));
                await Assert.ThrowsAsync<InvalidOperationException>(() => h.Switcher.UpdateAsync(both));
                IReadOnlyList<string> before = h.Snapshot();

                await Assert.ThrowsAsync<InvalidOperationException>(() => h.Switcher.RemoveExtensionAsync(another, both.Extensions[0].Extension));
                await Assert.ThrowsAsync<InvalidOperationException>(() => h.Switcher.UpdateAsync(AppSwitch.OfAll(another, null, both.Extensions)!));
                await Assert.ThrowsAsync<InvalidOperationException>(() => h.Deployer.ApplySettingsAsync(another, h.Reachable));

                h.AssertUnchangedSince(before);
                Assert.Equal(One, (await h.HostAsync()).Unfinished!.PutBack[0].Version);
            }
        }

        /// <summary>The service may still run the version that failed: the restart on the one put back is owed to the next change.</summary>
        [Fact]
        public async Task Update_DoesNotStayUpAndTheRestartOnThePreviousFails_TheNextChangeRestartsIt()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                h.AddHostRelease("2.0.0");
                h.ServiceManager.CrashOnNextStart.Add("shelf");
                h.ServiceManager.OnCrash = () => h.ServiceManager.RestartFailure = new InvalidOperationException("no restart today");
                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));
                Assert.Equal(new[] { Two }, (await h.HostAsync()).SkippedVersions);
                h.ServiceManager.RestartFailure = null;
                h.ForgetCalls();

                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                Assert.Contains("shelf: an earlier run was cut off before it restarted the service; restarting it.", h.Output.Messages);
                Assert.Equal(1, h.HostRestarts());
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        /// <summary>The fallback itself worked, but that could not be saved: on disk, the switch is still unfinished.</summary>
        [Fact]
        public async Task UpdateAsync_AnotherProofOfAnAppWhoseFallbackCouldNotBeSaved_IsRefused()
        {
            using (AppHarness h = await WithAReleaseAddingTheServiceAsync())
            {
                PreparedApp prepared = await h.Switcher.PrepareAsync(await h.HostAsync());
                PreparedApp another = await h.Switcher.PrepareAsync(prepared.App);
                DeployableVersion staged = await h.Deployer.StageAsync(prepared.App, Two, h.Client);
                h.ServiceManager.CrashOnNextStart.Add("shelf");
                h.SwitcherConfig.NextSaves.Enqueue(new IOException("disk full"));
                await Assert.ThrowsAsync<NotRunningException>(() => h.Switcher.UpdateAsync(AppSwitch.ToVersion(prepared, staged)));

                await Assert.ThrowsAsync<InvalidOperationException>(() => h.Switcher.UpdateAsync(AppSwitch.ToVersion(another, staged)));

                Assert.Equal(One, (await h.HostAsync()).Unfinished!.PutBack[0].Version);
                Assert.Equal(One, await h.Versions.GetCurrentAsync(Shelf));
            }
        }

        [Fact]
        public async Task RollBackAsync_ExtensionNotInstalled_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                IReadOnlyList<string> before = h.Snapshot();
                PreparedApp prepared = await h.Switcher.PrepareAsync(await h.HostAsync());

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => h.Switcher.RollBackAsync(AppSwitch.OfExtension(prepared, new InstalledExtension { Id = "images" }, One)));

                Assert.Equal("shelf/images is not installed.", ex.Message);
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task OfAll_NothingToSwitch_IsNoSwitch()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();

                Assert.Null(AppSwitch.OfAll(await h.Switcher.PrepareAsync(await h.HostAsync()), null, Array.Empty<ExtensionSwitch>()));
            }
        }
    }
}
