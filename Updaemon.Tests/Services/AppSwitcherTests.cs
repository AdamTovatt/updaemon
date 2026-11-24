using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;
using static Updaemon.Tests.Helpers.ShelfPackages;

namespace Updaemon.Tests.Services
{
    /// <summary>Switching an app and its extensions, and what becomes of a switch that fails.</summary>
    public class AppSwitcherTests
    {
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

        [Fact]
        public async Task Update_ToAVersionWithoutTheServiceCannotBeRecorded_EnablesAndStartsTheServiceAgain()
        {
            using (AppHarness h = await WithAReleaseWithoutTheServiceAsync())
            {
                h.SavedConfig.FailNextWithNothingLeft();

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
                h.SavedConfig.FailNextWithNothingLeft();

                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.True(File.Exists(h.Scope.GetUnitFilePath("shelf")));
                Assert.False(h.ServiceManager.ServiceRunningStates["shelf"]);
                Assert.DoesNotContain("shelf", h.ServiceManager.EnabledServices);
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
            PreparedApp prepared = await h.Preparer.PrepareAsync(await h.HostAsync());
            InstalledExtension images = prepared.App.FindExtension("images")!;
            DeployableVersion staged = await h.Deployer.StageAsync(prepared.App, Two, h.Client);
            await h.Versions.DownloadAsync(Images, images, Two, h.Client);
            return AppSwitch.Update(prepared, staged, await AcceptAsync(h, staged, Two));
        }

        private static async Task<AcceptedExtension> AcceptAsync(AppHarness h, DeployableVersion host, Version images) =>
            Assert.IsType<AcceptedExtension>(await h.Check.AskAsync(await h.HostAsync(), host, Images, images));

        private static async Task<AcceptedExtension> AcceptAsync(AppHarness h, Version images) => await AcceptAsync(h, await h.Deployer.OpenCurrentAsync("shelf"), images);

        /// <summary>What the commands say of a switch that went back.</summary>
        private static async Task<string?> GoesBackAsync(AppHarness h, AppSwitch change) => SwitchText.Problem("shelf", await h.Switcher.SwitchAsync(change));

        /// <summary>For the reason that the entry is not what it was when it was prepared.</summary>
        private static async Task AssertIsRefusedAsync(Func<Task> change)
        {
            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(change);
            Assert.Equal("shelf was changed since it was prepared; prepare it again.", ex.Message);
        }

        private static async Task<(Version Host, string Images)> CurrentAsync(AppHarness h) =>
            (await h.Versions.GetCurrentAsync(new AppName("shelf")), Assert.Single(h.LinkedExtensions()));

        /// <summary>One from install flags is all that tells how to run a release without its own.</summary>
        [Fact]
        public async Task Update_ReleaseWithoutAManifest_KeepsTheOneFromTheInstallFlags()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/tool", "1.0.0", manifest: null, "tool");
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "tool", "--from", "github:owner/tool", "--command", "tool" }));

                foreach (string version in new[] { "2.0.0", "3.0.0" })
                {
                    h.AddRelease("owner/tool", version, manifest: null, "tool");
                    Assert.Equal(0, await h.Update().ExecuteAsync(Array.Empty<string>()));
                    Assert.Equal("tool", (await h.Config.GetAppAsync("tool"))!.Manifest!.Command);
                }
            }
        }

        /// <summary>It runs again and its version is skipped: only the note of its restart could not be taken away.</summary>
        [Fact]
        public async Task Update_FallbackIsDoneButTheNoteOfItCannotBeCleared_WarnsAndStillChecksTheService()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                h.AddHostRelease("2.0.0");
                h.ServiceManager.CrashOnNextStart.Add("shelf");
                h.SavedConfig.FailNextWithNothingLeft();

                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal(0, h.SavedConfig.StillToFail);
                Assert.Equal(
                    new[]
                    {
                        "Warning: the next change to shelf restarts it once more: disk full",
                        "Error: shelf: 2.0.0 did not stay running, so 1.0.0 runs again and 2.0.0 is skipped. See 'updaemon logs shelf'.",
                    },
                    h.Output.Errors);
                Assert.Equal(new UnfinishedChange.CarryOut { Restart = true }, (await h.HostAsync()).Unfinished);
            }
        }

        [Fact]
        public async Task Rollback_NoteOfTheRestartCannotBeCleared_WarnsAndSucceeds()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                h.AddHostRelease("2.0.0");
                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));
                h.SavedConfig.FailNextWithNothingLeft();

                Assert.Equal(0, await h.RollbackCommand().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal(new[] { "Warning: the next change to shelf restarts it once more: disk full" }, h.Output.Errors);
                Assert.Equal(One, await h.Versions.GetCurrentAsync(Shelf));
            }
        }

        /// <summary>It is not in the entry, so there is no version of it to go back to: its link is to go.</summary>
        [Fact]
        public async Task Install_DoesNotStayUpAndCannotBeUndone_SaysSo()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.ServiceManager.CrashOnNextStart.Add("shelf");
                h.ServiceManager.OnCrash = () => h.ServiceManager.RestartFailure = new InvalidOperationException("no restart today.");

                Assert.Equal(1, await h.Install().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(
                    new[] { "Error: installing shelf/images failed: shelf did not stay running with 1.0.0, and going back failed: no restart today. See 'updaemon logs shelf'." },
                    h.Output.Errors);
            }
        }

        [Fact]
        public async Task SwitchAsync_HostAndExtension_SwitchesBothWithOneRestart()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                string imagesAtRestart = string.Empty;
                h.ServiceManager.OnRestart = () => imagesAtRestart = Assert.Single(h.LinkedExtensions());

                await h.Switcher.SwitchAsync(both);

                Assert.Equal("images -> 2.0.0", imagesAtRestart);
                Assert.Equal((Two, "images -> 2.0.0"), await CurrentAsync(h));
                Assert.Equal(1, h.HostRestarts());
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        [Fact]
        public async Task SwitchAsync_HostAndExtensionDoNotStayUp_PutsBothBackAndSkipsBoth()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                h.ServiceManager.CrashOnNextStart.Add("shelf");

                string? problem = await GoesBackAsync(h, both);

                Assert.Equal(
                    "shelf did not stay running with shelf 2.0.0, shelf/images 2.0.0, so shelf 1.0.0, shelf/images 1.0.0 run again and each of the new versions is skipped. See 'updaemon logs shelf'.",
                    problem);
                Assert.Equal((One, "images -> 1.0.0"), await CurrentAsync(h));
                InstalledApp host = await h.HostAsync();
                Assert.Equal(new[] { Two }, host.SkippedVersions);
                Assert.Equal(new[] { Two }, host.FindExtension("images")!.SkippedVersions);
                Assert.Null(host.Unfinished);
                Assert.Equal(2, h.HostRestarts());
            }
        }

        [Fact]
        public async Task SwitchAsync_NeitherStaysUp_SaysWhatIsCurrent()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                h.ServiceManager.CrashOnNextStart.Add("shelf");
                h.ServiceManager.OnCrash = () => h.ServiceManager.CrashOnNextStart.Add("shelf");

                string? problem = await GoesBackAsync(h, both);

                Assert.Equal(
                    "shelf did not stay running with shelf 2.0.0, shelf/images 2.0.0; shelf 1.0.0, shelf/images 1.0.0 are current again but did not stay running either, and each of the new versions is skipped. See 'updaemon logs shelf'.",
                    problem);
            }
        }

        [Fact]
        public async Task SwitchAsync_InstalledExtensionGoneAgainAndStillNotUp_SaysSo()
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
        public async Task SwitchAsync_OneOfTwoCannotBePutBack_PutsTheOtherBack()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                h.ServiceManager.OnRestart = () => Directory.Delete(h.Versions.GetVersionDirectory(new AppName("shelf"), One), recursive: true);
                h.SavedConfig.FailNextWithNothingLeft();

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Switcher.SwitchAsync(both));

                Assert.Equal("disk full, and switching back failed: shelf 1.0.0 is not kept. Kept versions: 2.0.0.", ex.Message);
                Assert.Equal((Two, "images -> 1.0.0"), await CurrentAsync(h));
                Assert.IsType<UnfinishedChange.PutBack>((await h.HostAsync()).Unfinished);
            }
        }

        [Fact]
        public async Task SwitchAsync_ExtensionAlone_DoesNotStayUp_SkipsItsVersion()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                h.ServiceManager.CrashOnNextStart.Add("shelf");

                string? problem = await GoesBackAsync(h, AppSwitch.UpdateExtensions(both.Prepared, await AcceptAsync(h, Two)));

                Assert.Equal("shelf did not stay running with 2.0.0, so 1.0.0 runs again and 2.0.0 is skipped. See 'updaemon logs shelf'.", problem);
                Assert.Equal((One, "images -> 1.0.0"), await CurrentAsync(h));
                InstalledApp host = await h.HostAsync();
                Assert.Empty(host.SkippedVersions);
                Assert.Equal(new[] { Two }, host.FindExtension("images")!.SkippedVersions);
            }
        }

        /// <summary>After a failure the entry in hand is not what is saved: saving it would bring back what was put back.</summary>
        [Fact]
        public async Task SwitchAsync_AgainWithTheSameProof_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                h.SavedConfig.FailNextWithNothingLeft();
                await Assert.ThrowsAsync<IOException>(() => h.Switcher.SwitchAsync(both));
                IReadOnlyList<string> before = h.Snapshot();

                await AssertIsRefusedAsync(() => h.Switcher.SwitchAsync(both));
                await AssertIsRefusedAsync(() => h.Switcher.SwitchAsync(AppSwitch.Remove(both.Prepared, both.App.Extensions[0])));
                await AssertIsRefusedAsync(() => both.Prepared.BeginChangeAsync());

                h.AssertUnchangedSince(before);
                Assert.Equal((One, "images -> 1.0.0"), await CurrentAsync(h));
            }
        }

        [Fact]
        public async Task SwitchAsync_AgainWithTheSameProofAfterOneThatStands_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                await h.Switcher.SwitchAsync(both);
                IReadOnlyList<string> before = h.Snapshot();

                AcceptedExtension one = await AcceptAsync(h, One);
                await AssertIsRefusedAsync(() => h.Switcher.SwitchAsync(AppSwitch.UpdateExtensions(both.Prepared, one)));

                h.AssertUnchangedSince(before);
                Assert.Equal((Two, "images -> 2.0.0"), await CurrentAsync(h));
            }
        }

        /// <summary>What to put back is only saved once: a second switch would overwrite it with what the first one left.</summary>
        [Fact]
        public async Task SwitchAsync_AnotherProofOfAnAppWhoseSwitchCouldNotBePutBack_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                PreparedApp another = await h.Preparer.PrepareAsync(both.App);
                h.ServiceManager.OnRestart = () => Directory.Delete(h.Versions.GetVersionDirectory(new AppName("shelf"), One), recursive: true);
                h.SavedConfig.FailNextWithNothingLeft();
                InvalidOperationException notBack = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Switcher.SwitchAsync(both));
                Assert.StartsWith("disk full, and switching back failed", notBack.Message);
                IReadOnlyList<string> before = h.Snapshot();

                await AssertIsRefusedAsync(() => h.Switcher.SwitchAsync(AppSwitch.Remove(another, both.App.Extensions[0])));
                await AssertIsRefusedAsync(() => h.Switcher.SwitchAsync(AppSwitch.UpdateExtensions(another, both.Extensions[0].To!)));
                await AssertIsRefusedAsync(() => another.BeginChangeAsync());

                h.AssertUnchangedSince(before);
                Assert.Equal(One, Assert.IsType<UnfinishedChange.PutBack>((await h.HostAsync()).Unfinished).Versions[0].Version);
            }
        }

        /// <summary>The fallback itself worked, but that could not be saved: on disk, the switch is still unfinished.</summary>
        [Fact]
        public async Task SwitchAsync_AnotherProofOfAnAppWhoseFallbackCouldNotBeSaved_IsRefused()
        {
            using (AppHarness h = await WithAReleaseAddingTheServiceAsync())
            {
                PreparedApp prepared = await h.Preparer.PrepareAsync(await h.HostAsync());
                PreparedApp another = await h.Preparer.PrepareAsync(prepared.App);
                DeployableVersion staged = await h.Deployer.StageAsync(prepared.App, Two, h.Client);
                h.ServiceManager.CrashOnNextStart.Add("shelf");
                h.SavedConfig.FailNextWithNothingLeft();
                Assert.IsType<SwitchOutcome.WentBack>(await h.Switcher.SwitchAsync(AppSwitch.Update(prepared, staged)));

                await AssertIsRefusedAsync(() => h.Switcher.SwitchAsync(AppSwitch.Update(another, staged)));

                Assert.Equal(One, Assert.IsType<UnfinishedChange.PutBack>((await h.HostAsync()).Unfinished).Versions[0].Version);
                Assert.Equal(One, await h.Versions.GetCurrentAsync(Shelf));
            }
        }

        /// <summary>Another change may have come to stand meanwhile: committing this one's copy would undo it.</summary>
        [Fact]
        public async Task SwitchAsync_AnotherProofOfAnAppThatWasChangedSince_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                PreparedApp another = await h.Preparer.PrepareAsync(both.App);
                Assert.Equal(0, await h.SetArgs().ExecuteAsync(new[] { "shelf", "--", "other" }));
                IReadOnlyList<string> before = h.Snapshot();

                await AssertIsRefusedAsync(() => another.BeginChangeAsync());
                await AssertIsRefusedAsync(() => h.Switcher.SwitchAsync(AppSwitch.Remove(another, both.App.Extensions[0])));

                h.AssertUnchangedSince(before);
            }
        }

        /// <summary>Asked of one version of the host, while another will run.</summary>
        [Fact]
        public async Task SwitchAsync_ExtensionAcceptedByAnotherVersionThanWillRun_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                AppSwitch both = await SwitchOfBothAsync(h);
                AcceptedExtension byCurrent = await AcceptAsync(h, Two);
                IReadOnlyList<string> before = h.Snapshot();

                InvalidOperationException newHost = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => h.Switcher.SwitchAsync(AppSwitch.Update(both.Prepared, both.NewVersion!, byCurrent)));
                InvalidOperationException alone = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => h.Switcher.SwitchAsync(AppSwitch.RollBack(both.Prepared, both.Extensions[0].To!)));

                Assert.Equal("shelf/images 2.0.0 was accepted by shelf 1.0.0, but 2.0.0 will run.", newHost.Message);
                Assert.Equal("shelf/images 2.0.0 was accepted by shelf 2.0.0, but 1.0.0 will run.", alone.Message);
                h.AssertUnchangedSince(before);
            }
        }

        /// <summary>One without a current version has no link to put back, but stays installed with the versions it has.</summary>
        [Fact]
        public async Task Remove_ExtensionWithoutACurrentVersionCannotBeRecorded_KeepsItsVersions()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                h.Versions.Unlink(Images);
                IReadOnlyList<string> before = h.Snapshot();
                h.SavedConfig.FailNext(app => app.Unfinished is not UnfinishedChange.PutBack);

                await Assert.ThrowsAsync<IOException>(() => h.Remove().ExecuteAsync(new[] { "shelf/images" }));

                h.AssertUnchangedSince(before);
            }
        }

        /// <summary>What the host runs with does not change by it.</summary>
        [Fact]
        public async Task Remove_Extension_WritesNoFileOfTheHost()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                await h.SetRestart().ExecuteAsync(new[] { "shelf", "manual" });
                await File.WriteAllTextAsync(Path.Combine(h.Scope.GetCurrentLinkPath("shelf"), AppManifest.FileName), "not json");

                Assert.Equal(0, await h.Remove().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Empty(h.LinkedExtensions());
                Assert.DoesNotContain(MockServiceManager.Call(nameof(IServiceManager.DaemonReloadAsync)), h.ServiceManager.MethodCalls);
            }
        }

        [Fact]
        public async Task RollBack_ExtensionNotInstalled_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                await h.Versions.DownloadAsync(Images, new InstalledExtension { Id = "images", Remote = ImagesRemote }, One, h.Client);
                AcceptedExtension accepted = await AcceptAsync(h, One);
                PreparedApp prepared = await h.Preparer.PrepareAsync(await h.HostAsync());

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => AppSwitch.RollBack(prepared, accepted));

                Assert.Equal("shelf/images is not installed.", ex.Message);
            }
        }

        [Fact]
        public async Task UpdateExtensions_None_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();

                PreparedApp prepared = await h.Preparer.PrepareAsync(await h.HostAsync());

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => AppSwitch.UpdateExtensions(prepared));

                Assert.Equal("There is no extension of shelf to switch.", ex.Message);
            }
        }
    }
}
