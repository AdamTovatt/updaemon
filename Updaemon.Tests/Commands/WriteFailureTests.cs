using System.Text.RegularExpressions;
using Updaemon.Models;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Commands
{
    /// <summary>
    /// A full disk or an unwritable directory partway through install, update and rollback: the current
    /// version stays as it was, nothing new is left behind, and the error names the path that failed.
    /// Directories are made unwritable for real, so these skip under root.
    /// </summary>
    public class WriteFailureTests
    {
        /// <summary>A release whose every generated file differs from 1.0.0's, so activating it has to write each one.</summary>
        private const string ChangedManifest = """
            {
              "executable": "bin/byteshelf2",
              "command": "byteshelf",
              "service": { "args": ["sync", "run", "--fast"] },
              "settings": {
                "BYTESHELF_URL": { "required": true },
                "BYTESHELF_API_KEY": { "required": true, "secret": true },
                "BYTESHELF_LOG_LEVEL": { "default": "info" },
                "BYTESHELF_THREADS": { "default": "4" }
              }
            }
            """;

        [Fact]
        public async Task Update_DiskFullWhileDownloading_KeepsTheCurrentVersionAndLeavesNothing()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "2.0.0", ChangedManifest, "bin/byteshelf2");
                string partial = string.Empty;
                h.AfterDownloading("owner/byteshelf", "2.0.0", directory =>
                {
                    partial = Path.Combine(directory, "bin/byteshelf2");
                    throw new IOException($"No space left on device : '{partial}'");
                });
                IReadOnlyList<string> before = h.Snapshot();

                int exitCode = await h.Update().ExecuteAsync(new[] { "byteshelf" });

                Assert.Equal(1, exitCode);
                Assert.Equal($"Error: byteshelf: No space left on device : '{partial}'", Assert.Single(h.Output.Errors));
                h.AssertUnchangedSince(before);
            }
        }

        public static TheoryData<string> GeneratedFileDirectories => new TheoryData<string> { "settings", "units", "commands" };

        private static string DirectoryOf(AppHarness h, string kind) => kind switch
        {
            "settings" => Path.GetDirectoryName(h.Scope.GetSettingsFilePath("byteshelf"))!,
            "units" => h.Scope.UnitDirectory,
            _ => h.Scope.BinDirectory,
        };

        [SkippableTheory]
        [MemberData(nameof(GeneratedFileDirectories))]
        public async Task Update_DirectoryUnwritableWhileActivating_KeepsTheCurrentVersionAndLeavesNothing(string kind)
        {
            Unwritable.SkipUnderRoot();
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "2.0.0", ChangedManifest, "bin/byteshelf2");
                IReadOnlyList<string> before = h.Snapshot();
                string directory = DirectoryOf(h, kind);

                int exitCode = await Unwritable.WhileAsync(directory, () => h.Update().ExecuteAsync(new[] { "byteshelf" }));

                Assert.Equal(1, exitCode);
                string error = Assert.Single(h.Output.Errors);
                Assert.StartsWith($"Error: byteshelf: Could not write {directory}/", error);
                Assert.Equal(new Version(1, 0, 0), await h.Versions.GetCurrentAsync(new AppName("byteshelf")));
                h.AssertUnchangedSince(before);
            }
        }

        [SkippableFact]
        public async Task Install_DirectoryUnwritable_LeavesNothing()
        {
            Unwritable.SkipUnderRoot();
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                // As on a machine with apps installed: the shared directories are there, and stay.
                foreach (string shared in new[]
                {
                    h.Scope.UnitDirectory, h.Scope.AppsDirectory, h.Scope.BinDirectory, h.Scope.SecretsDirectory,
                    Path.GetDirectoryName(h.Scope.GetSettingsFilePath("byteshelf"))!, Path.GetDirectoryName(h.Scope.GetDataDirectory("byteshelf"))!,
                })
                {
                    Directory.CreateDirectory(shared);
                }

                IReadOnlyList<string> before = h.Snapshot();

                int exitCode = await Unwritable.WhileAsync(h.Scope.UnitDirectory, () => h.InstallByteShelfAsync());

                Assert.Equal(1, exitCode);
                Assert.StartsWith($"Error: installing byteshelf failed: Could not write {h.Scope.UnitDirectory}/", Assert.Single(h.Output.Errors));
                h.AssertUnchangedSince(before);
            }
        }

        /// <summary>byteshelf with 2.0.0 current and 1.0.0 kept.</summary>
        private static async Task<AppHarness> UpdatedToTwoAsync()
        {
            AppHarness h = new AppHarness();
            await h.InstallByteShelfAsync();
            h.AddRelease("owner/byteshelf", "2.0.0", ChangedManifest, "bin/byteshelf2");
            Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "byteshelf" }));
            h.Output.Clear();
            return h;
        }

        /// <summary>Saved neither before nor after the switch: the first never begins it, the second has to undo it.</summary>
        [SkippableTheory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Rollback_ConfigCannotBeSaved_SwitchesBackAndChangesNothing(bool onceSwitched)
        {
            Unwritable.SkipUnderRoot();
            using (AppHarness h = await UpdatedToTwoAsync())
            using (Unwritable.Pending unwritable = Unwritable.Prepare(h.Scope.RootDirectory))
            {
                IReadOnlyList<string> before = h.Snapshot();
                if (onceSwitched)
                {
                    int reloads = 0;
                    // Unwritable once switched, and writable again by the time it is switched back.
                    h.ServiceManager.OnDaemonReload = () => (reloads++ == 0 ? unwritable.Make : (Action)unwritable.Dispose)();
                }
                else
                {
                    unwritable.Make();
                }

                IOException ex = await Assert.ThrowsAsync<IOException>(() => h.RollbackCommand().ExecuteAsync(new[] { "byteshelf" }));
                unwritable.Dispose();

                Assert.StartsWith($"Could not write {h.Scope.ConfigFilePath}: ", ex.Message);
                Assert.Equal(new Version(2, 0, 0), await h.Versions.GetCurrentAsync(new AppName("byteshelf")));
                Assert.Empty((await h.Config.GetAppAsync("byteshelf"))!.SkippedVersions);
                h.AssertUnchangedSince(before);
            }
        }

        [SkippableFact]
        public async Task Rollback_ConfigCannotBeSavedNorSwitchedBack_SaysBoth()
        {
            Unwritable.SkipUnderRoot();
            using (AppHarness h = await UpdatedToTwoAsync())
            using (Unwritable.Pending unwritable = Unwritable.Prepare(h.Scope.RootDirectory))
            {
                h.ServiceManager.OnDaemonReload = () =>
                {
                    // Once switched, it cannot be recorded, and the version to switch back to is gone.
                    unwritable.Make();
                    Directory.Delete(Path.Combine(h.Scope.GetAppDirectory("byteshelf"), "2.0.0"), recursive: true);
                };

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.RollbackCommand().ExecuteAsync(new[] { "byteshelf", "1.0.0" }));

                Assert.Matches($"^Could not write {Regex.Escape(h.Scope.ConfigFilePath)}: .*, and switching back failed: byteshelf 2\\.0\\.0 is not kept\\.", ex.Message);
            }
        }

        [SkippableFact]
        public async Task Update_NewVersionDoesNotStayRunningAndItsSkipCannotBeSaved_StillRunsThePrevious()
        {
            Unwritable.SkipUnderRoot();
            using (AppHarness h = new AppHarness())
            using (Unwritable.Pending unwritable = Unwritable.Prepare(h.Scope.RootDirectory))
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "1.1.0", AppHarness.ByteShelfManifest);
                h.ServiceManager.CrashOnNextStart.Add("byteshelf");
                h.ServiceManager.OnCrash = unwritable.Make;

                int exitCode = await h.Update().ExecuteAsync(new[] { "byteshelf" });

                Assert.Equal(1, exitCode);
                Assert.Matches(
                    $"^Error: byteshelf: 1\\.1\\.0 did not stay running, so 1\\.0\\.0 runs again and 1\\.1\\.0 could not be recorded as skipped \\(Could not write {Regex.Escape(h.Scope.ConfigFilePath)}: .*\\), so the next update tries it again\\. See 'updaemon logs byteshelf'\\.$",
                    Assert.Single(h.Output.Errors));
                // 1.1.0 is known not to run: going back stands, unlike a rollback by hand.
                Assert.Equal(new Version(1, 0, 0), await h.Versions.GetCurrentAsync(new AppName("byteshelf")));
                Assert.Empty((await h.Config.GetAppAsync("byteshelf"))!.SkippedVersions);
            }
        }
    }
}
