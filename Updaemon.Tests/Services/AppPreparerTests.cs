using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;
using static Updaemon.Tests.Helpers.ShelfPackages;

namespace Updaemon.Tests.Services
{
    /// <summary>How the next change to an app puts right what a run left unfinished.</summary>
    public class AppPreparerTests
    {
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
            Assert.IsType<UnfinishedChange.PutBack>((await h.HostAsync()).Unfinished);
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

                Assert.Contains("shelf: an earlier run did not finish switching versions; putting back shelf 1.0.0.", h.Output.Messages);
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
                Assert.IsType<UnfinishedChange.PutBack>((await h.HostAsync()).Unfinished);

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
                Assert.Equal(new UnfinishedChange.CarryOut { Restart = true }, (await h.HostAsync()).Unfinished);
            }
        }

        public static TheoryData<string, Func<AppHarness, Task>, Func<AppHarness, Task<int>>, int> ChangesThatDoNotRestart => new TheoryData<string, Func<AppHarness, Task>, Func<AppHarness, Task<int>>, int>
        {
            { "set of a disabled service", h => h.ServiceManager.DisableServiceAsync("shelf"), h => h.Set().ExecuteAsync(new[] { "shelf", "A=1" }), 0 },
            { "update with manual restart", h => h.SetRestart().ExecuteAsync(new[] { "shelf", "manual" }), h => h.Update().ExecuteAsync(new[] { "shelf" }), 0 },
            { "removal with manual restart", h => h.SetRestart().ExecuteAsync(new[] { "shelf", "manual" }), h => h.Remove().ExecuteAsync(new[] { "shelf/images" }), 0 },
            {
                "update that cannot be made",
                h =>
                {
                    File.WriteAllText(h.Scope.GetCommandPath("shelf2"), "someone else's");
                    return Task.CompletedTask;
                },
                h => h.Update().ExecuteAsync(new[] { "shelf" }),
                1
            },
        };

        /// <summary>The service may still run the version given up: a change that does not restart it leaves that to the next one.</summary>
        [Theory]
        [MemberData(nameof(ChangesThatDoNotRestart))]
        public async Task KilledUpdate_RestartOfWhatIsPutBackFails_StaysOwed(string name, Func<AppHarness, Task> arrange, Func<AppHarness, Task<int>> change, int exitCode)
        {
            _ = name; // Names the case in the test's display name.
            (AppHarness h, _) = await KilledUpdateAsync(harness => harness.ServiceManager.OnRestart = harness.KeepForCutOff);
            using (h)
            {
                h.Client.RefusedRemotes.Clear();
                h.ServiceManager.RestartFailure = new InvalidOperationException("no restart today");
                await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" });
                await arrange(h);

                Assert.Equal(exitCode, await change(h));
                Assert.Equal(new UnfinishedChange.CarryOut { Restart = true }, (await h.HostAsync()).Unfinished);

                h.ServiceManager.RestartFailure = null;
                h.ForgetCalls();
                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));
                Assert.Contains("shelf: an earlier run did not finish restarting the service; restarting it.", h.Output.Messages);
                Assert.Equal(1, h.HostRestarts());
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        public static TheoryData<string, Func<AppHarness, Task<int>>> ChangesThatRestart => new TheoryData<string, Func<AppHarness, Task<int>>>
        {
            { "set", h => h.Set().ExecuteAsync(new[] { "shelf", "A=1" }) },
            { "update", h => h.Update().ExecuteAsync(new[] { "shelf" }) },
            { "removal", h => h.Remove().ExecuteAsync(new[] { "shelf/images" }) },
        };

        /// <summary>A change whose own restart works pays what was owed.</summary>
        [Theory]
        [MemberData(nameof(ChangesThatRestart))]
        public async Task KilledUpdate_RestartOfWhatIsPutBackFails_TheChangesOwnRestartIsEnough(string name, Func<AppHarness, Task<int>> change)
        {
            _ = name; // Names the case in the test's display name.
            (AppHarness h, _) = await KilledUpdateAsync(harness => harness.ServiceManager.OnRestart = harness.KeepForCutOff);
            using (h)
            {
                h.Client.RefusedRemotes.Clear();
                h.ServiceManager.RestartFailure = new InvalidOperationException("no restart today");
                await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" });
                h.ServiceManager.OnRestart = () => h.ServiceManager.OnRestart = () => h.ServiceManager.RestartFailure = null;
                h.ForgetCalls();

                Assert.Equal(0, await change(h));

                // The one that was owed, which fails again, and the change's own.
                Assert.Equal(2, h.HostRestarts());
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        /// <summary>What it is put back on, it is restarted on: that pays what was owed too.</summary>
        [Fact]
        public async Task KilledUpdate_RestartOfWhatIsPutBackFails_TheRestartOfACancelledUpdateIsEnough()
        {
            (AppHarness h, _) = await KilledUpdateAsync(harness => harness.ServiceManager.OnRestart = harness.KeepForCutOff);
            using (h)
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                h.Client.RefusedRemotes.Clear();
                h.ServiceManager.RestartFailure = new InvalidOperationException("no restart today");
                await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" });
                h.ServiceManager.OnRestart = () => h.ServiceManager.OnRestart = () =>
                {
                    h.ServiceManager.RestartFailure = null;
                    cancellation.Cancel();
                };

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Update().ExecuteAsync(new[] { "shelf" }, cancellation.Token));

                Assert.Equal(One, await h.Versions.GetCurrentAsync(Shelf));
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        /// <summary>The removed version's command is not in the manifest that is current, which is all there is to read.</summary>
        [Fact]
        public async Task KilledUpdate_CannotBePutBack_RemovingTheAppDeletesTheCommandsOfBothVersions()
        {
            (AppHarness h, _) = await KilledUpdateAsync(harness => harness.ServiceManager.OnRestart = harness.KeepForCutOff);
            using (h)
            {
                File.Copy(h.Scope.GetCommandPath("shelf2"), h.Scope.GetCommandPath("shelf"));
                string link = h.Scope.GetCommandPath("mine");
                File.CreateSymbolicLink(link, h.Scope.GetCommandPath("shelf"));
                File.WriteAllText(h.Scope.GetCommandPath("other"), "someone else's");
                Directory.Delete(h.Versions.GetVersionDirectory(Shelf, One), recursive: true);

                Assert.Equal(0, await h.Remove().ExecuteAsync(new[] { "shelf" }));

                Assert.StartsWith("Warning: putting back shelf 1.0.0 failed", Assert.Single(h.Output.Errors));
                Assert.Equal(new[] { link, h.Scope.GetCommandPath("other") }, Directory.GetFileSystemEntries(h.Scope.BinDirectory).Order(StringComparer.Ordinal));
            }
        }

        /// <summary>The unit, which removing it needs the service manager for, is written before the reload the run was killed in.</summary>
        [Fact]
        public async Task KilledUpdate_ThatAddsTheService_ChecksTheServiceManagerBeforeItPutsBack()
        {
            using (AppHarness h = await WithAReleaseAddingTheServiceAsync())
            {
                h.ServiceManager.OnDaemonReload = h.KeepForCutOff;
                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));
                h.CutOff();
                // As another killed run left it, which had deleted it but not told the service manager.
                File.Delete(h.Scope.GetUnitFilePath("shelf"));
                h.ServiceManager.Unreachable = "unreachable";
                IReadOnlyList<string> before = h.Snapshot();

                await Assert.ThrowsAsync<ServiceManagerUnreachableException>(() => h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));
                h.AssertUnchangedSince(before);

                h.ServiceManager.Unreachable = null;
                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));
                Assert.False(File.Exists(h.Scope.GetServiceWrapperPath("shelf")));
                Assert.Contains(MockServiceManager.Call(nameof(IServiceManager.DaemonReloadAsync)), h.ServiceManager.MethodCalls);
            }
        }

        /// <summary>Nothing later in the removal asks for the service manager, as no unit is there.</summary>
        [Fact]
        public async Task KilledUpdate_ThatRemovedTheService_RemovingTheAppNeedsTheServiceManager()
        {
            using (AppHarness h = await WithAReleaseWithoutTheServiceAsync())
            {
                h.ServiceManager.OnDaemonReload = h.KeepForCutOff;
                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));
                h.CutOff();
                h.ServiceManager.Unreachable = "unreachable";
                IReadOnlyList<string> before = h.Snapshot();

                await Assert.ThrowsAsync<ServiceManagerUnreachableException>(() => h.Remove().ExecuteAsync(new[] { "shelf" }));

                h.AssertUnchangedSince(before);
            }
        }

        /// <summary>It was not to be restarted, so it is not for having been killed either.</summary>
        [Fact]
        public async Task KilledUpdate_WithManualRestart_IsPutBackWithoutARestart()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                await h.SetRestart().ExecuteAsync(new[] { "shelf", "manual" });
                h.AddHostRelease("2.0.0", ChangedManifest);
                h.ServiceManager.OnDaemonReload = h.KeepForCutOff;
                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));
                h.CutOff();

                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                Assert.Equal(One, await h.Versions.GetCurrentAsync(Shelf));
                Assert.Equal(0, h.HostRestarts());
            }
        }

        [Fact]
        public async Task KilledSet_OfADisabledService_WritesItsFilesWithoutARestart()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                await h.ServiceManager.DisableServiceAsync("shelf");
                h.SavedConfig.OnSaved = app =>
                {
                    if (app.Settings.ContainsKey("A"))
                    {
                        h.KeepForCutOff();
                    }
                };
                Assert.Equal(0, await h.Set().ExecuteAsync(new[] { "shelf", "A=1" }));
                h.CutOff();
                h.ServiceManager.MethodCalls.Clear();

                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                Assert.Contains("A=", File.ReadAllText(h.Scope.GetSettingsFilePath("shelf")));
                Assert.Equal(0, h.HostRestarts());
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

                Assert.Contains("shelf: an earlier run did not finish switching versions; putting back no shelf/images.", h.Output.Messages);
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
                h.SavedConfig.OnSave = app =>
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

                Assert.Contains("shelf: an earlier run did not finish switching versions; putting back shelf/images 1.0.0.", h.Output.Messages);
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

                Assert.Contains("shelf: an earlier run did not finish restarting the service; restarting it.", h.Output.Messages);
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

        [Fact]
        public async Task Update_PuttingBackARemovedServiceFails_TheNextChangeStartsIt()
        {
            using (AppHarness h = await WithAReleaseWithoutTheServiceAsync())
            {
                h.SavedConfig.FailNextWithNothingLeft();
                h.ServiceManager.StartFailure = new InvalidOperationException("no start today");

                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal(new[] { "Error: shelf: disk full, and switching back failed: no start today." }, h.Output.Errors);
                Assert.False(h.ServiceManager.ServiceRunningStates["shelf"]);
                Assert.IsType<UnfinishedChange.PutBack>((await h.HostAsync()).Unfinished);

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
                h.SavedConfig.FailNextWithNothingLeft();
                h.SavedConfig.FailNextWithNothingLeft();
                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));
                Assert.IsType<UnfinishedChange.PutBack>((await h.HostAsync()).Unfinished);
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

                Assert.Contains("shelf: an earlier run did not finish restarting the service; restarting it.", h.Output.Messages);
                Assert.Equal(1, h.HostRestarts());
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        [Fact]
        public async Task KilledSet_OfACommandOnlyApp_NextChangeWritesItsFiles()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync(CommandOnlyManifest);
                h.SavedConfig.OnSaved = app =>
                {
                    if (app.Settings.ContainsKey("A"))
                    {
                        h.KeepForCutOff();
                    }
                };
                Assert.Equal(0, await h.Set().ExecuteAsync(new[] { "shelf", "A=1" }));
                h.CutOff();
                Assert.DoesNotContain("A=", File.ReadAllText(h.Scope.GetSettingsFilePath("shelf")));
                UnfinishedChange left = (await h.HostAsync()).Unfinished!;
                Assert.Equal(new UnfinishedChange.CarryOut { Restart = false }, left);

                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                Assert.Contains("shelf: an earlier run did not finish applying its settings; applying them.", h.Output.Messages);
                Assert.Contains("A=", File.ReadAllText(h.Scope.GetSettingsFilePath("shelf")));
                Assert.Equal(0, h.HostRestarts());
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        /// <summary>What is saved stands, but its files cannot follow: no change builds on that.</summary>
        [Fact]
        public async Task KilledSet_ThatCannotBeCarriedOut_OnlyRemoveGoesOn()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.ServiceManager.OnRestart = h.KeepForCutOff;
                Assert.Equal(0, await h.Set().ExecuteAsync(new[] { "shelf", "A=1" }));
                h.CutOff();
                File.WriteAllText(h.Scope.GetCommandPath("shelf"), "someone else's");

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                Assert.Equal(
                    $"restarting it failed: '{h.Scope.GetCommandPath("shelf")}' already exists and updaemon did not create it. Remove it, then try again. Until that works shelf cannot be changed; 'updaemon remove shelf' still removes it.",
                    ex.Message);
                Assert.IsType<UnfinishedChange.CarryOut>((await h.HostAsync()).Unfinished);
                Assert.Equal("1", (await h.HostAsync()).Settings["A"]);
                Assert.Equal(0, await h.Remove().ExecuteAsync(new[] { "shelf" }));
                Assert.Null(await h.Config.GetAppAsync("shelf"));
                Assert.Equal("someone else's", File.ReadAllText(h.Scope.GetCommandPath("shelf")));
            }
        }
    }
}
