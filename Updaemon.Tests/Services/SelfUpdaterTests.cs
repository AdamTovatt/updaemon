using Updaemon.Common.Utilities;
using Updaemon.Configuration;
using Updaemon.IO;
using Updaemon.Models;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    /// <summary>
    /// Self-update against a temp scope whose updaemon is a script, with releases that are scripts too.
    /// </summary>
    public class SelfUpdaterTests
    {
        private static readonly UpdaemonVersion Running = AppHarness.RunningVersion;
        private static readonly UpdaemonVersion Next = UpdaemonVersion.Of(1, 1, 0);

        private static AppHarness ScopeRunningItsOwnUpdaemon()
        {
            AppHarness h = new AppHarness();
            h.RunTheScopesOwnUpdaemon();
            return h;
        }

        private static string? LinkTarget(AppHarness h) => File.ResolveLinkTarget(h.Scope.UpdaemonCommandPath, returnFinalTarget: false)?.FullName;

        private static string NextDirectory(AppHarness h) => Path.GetDirectoryName(h.Scope.GetUpdaemonBinaryPath(Next))!;

        /// <summary>The old version is still linked, nothing of the new one is left, and the error names why.</summary>
        private static void AssertKeptTheOldOne(AppHarness h, string problem)
        {
            Assert.Equal(h.Scope.GetUpdaemonBinaryPath(Running), LinkTarget(h));
            Assert.False(Directory.Exists(NextDirectory(h)));
            Assert.Equal(new[] { $"Error: updaemon: updating to 1.1.0 failed: {problem}. 1.0.0 stays." }, h.Output.Errors);
        }

        [Fact]
        public async Task RunAsync_NewerRelease_InstallsItBesideAndSwitchesTheCommand()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.Publish(Next);

                bool succeeded = await h.SelfUpdateAsync();

                Assert.True(succeeded);
                Assert.Equal(h.Scope.GetUpdaemonBinaryPath(Next), LinkTarget(h));
                Assert.True(File.Exists(h.Scope.GetUpdaemonBinaryPath(Running)));
                Assert.Contains("Updated updaemon from 1.0.0 to 1.1.0.", h.Output.Messages);
                Assert.Empty(Directory.GetFiles(h.Scope.BinDirectory, "*.tmp"));
                Assert.Equal(new[] { h.Scope.GetUpdaemonBinaryPath(Next) }, Directory.GetFiles(NextDirectory(h)));
            }
        }

        [Fact]
        public async Task RunAsync_NewerRelease_LeavesItsDirectoryRunnableByEveryone()
        {
            // Proven under 'umask 077', where a directory is otherwise created 700.
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.Publish(Next);

                await h.SelfUpdateAsync();

                Assert.Equal(AtomicFile.Executable, File.GetUnixFileMode(NextDirectory(h)));
                Assert.Equal(AtomicFile.Executable, File.GetUnixFileMode(h.Scope.UpdaemonVersionsDirectory));
            }
        }

        [Fact]
        public async Task RunAsync_SecondUpdate_KeepsOnlyTheNewAndThePreviousVersion()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                string older = Path.Combine(h.Scope.UpdaemonVersionsDirectory, "0.9.0");
                string notAVersion = Path.Combine(h.Scope.UpdaemonVersionsDirectory, "leftover");
                Directory.CreateDirectory(older);
                Directory.CreateDirectory(notAVersion);
                h.Releases.Publish(Next);

                await h.SelfUpdateAsync();

                Assert.False(Directory.Exists(older));
                Assert.False(Directory.Exists(notAVersion));
                Assert.True(File.Exists(h.Scope.GetUpdaemonBinaryPath(Running)));
            }
        }

        [Fact]
        public async Task RunAsync_NewVersionPrintsAnotherVersion_KeepsTheOldOne()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.Publish(Next, printedVersion: "1.0.9");

                Assert.False(await h.SelfUpdateAsync());

                AssertKeptTheOldOne(h, "'updaemon --version' exited 0 printing '1.0.9' instead of '1.1.0'");
            }
        }

        [Fact]
        public async Task RunAsync_NewVersionExitsNonZero_KeepsTheOldOne()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.Publish(Next, exitCode: 3);

                Assert.False(await h.SelfUpdateAsync());

                AssertKeptTheOldOne(h, "'updaemon --version' exited 3 printing '1.1.0' instead of '1.1.0'");
            }
        }

        [Fact]
        public async Task RunAsync_NewVersionDoesNotRun_KeepsTheOldOne()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.PublishBuild(Next, "not a program");

                Assert.False(await h.SelfUpdateAsync());

                Assert.Equal(h.Scope.GetUpdaemonBinaryPath(Running), LinkTarget(h));
                Assert.False(Directory.Exists(NextDirectory(h)));
                Assert.Contains(h.Output.Errors, e => e.StartsWith("Error: updaemon: updating to 1.1.0 failed: it does not run: "));
            }
        }

        [Fact]
        public async Task RunAsync_NewVersionHangs_StopsItAndKeepsTheOldOne()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.PublishBuild(Next, "#!/bin/sh\nsleep 30\n");

                Assert.False(await h.SelfUpdateAsync(h.SelfUpdater(smokeRunTimeout: TimeSpan.FromMilliseconds(200))));

                AssertKeptTheOldOne(h, "'updaemon --version' did not finish within 0.2 seconds");
            }
        }

        [Fact]
        public async Task RunAsync_DownloadFails_KeepsTheOldOne()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.Publish(Next);
                h.Releases.DownloadFailure = new HttpRequestException("404 (Not Found).");

                Assert.False(await h.SelfUpdateAsync());

                AssertKeptTheOldOne(h, "the download failed: 404 (Not Found)");
            }
        }

        [Fact]
        public async Task RunAsync_DiskFullWhileDownloading_KeepsTheOldOne()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.Publish(Next);
                string download = h.Scope.GetUpdaemonBinaryPath(Next) + ".download";
                h.Releases.DownloadFailure = new IOException($"No space left on device : '{download}'");

                Assert.False(await h.SelfUpdateAsync());

                AssertKeptTheOldOne(h, $"No space left on device : '{download}'");
            }
        }

        [SkippableFact]
        public async Task RunAsync_CommandCannotBeSwitched_KeepsTheOldOneAndNoCopyOfTheNew()
        {
            Unwritable.SkipUnderRoot();
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.Publish(Next);

                Assert.False(await Unwritable.WhileAsync(h.Scope.BinDirectory, () => h.SelfUpdateAsync()));

                Assert.Equal(h.Scope.GetUpdaemonBinaryPath(Running), LinkTarget(h));
                Assert.False(Directory.Exists(NextDirectory(h)));
                Assert.StartsWith(
                    $"Error: updaemon: updating to 1.1.0 failed: Could not point '{h.Scope.UpdaemonCommandPath}' at '{h.Scope.GetUpdaemonBinaryPath(Next)}': ",
                    Assert.Single(h.Output.Errors));
            }
        }

        [Fact]
        public async Task RunAsync_BuildFailsItsChecksum_KeepsTheOldOne()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.Publish(Next);
                h.Releases.DownloadFailure = new ChecksumException("Updaemon-linux-arm64 does not match its SHA-256 in SHA256SUMS; the download is corrupt or was tampered with.");

                Assert.False(await h.SelfUpdateAsync());

                AssertKeptTheOldOne(h, "Updaemon-linux-arm64 does not match its SHA-256 in SHA256SUMS; the download is corrupt or was tampered with");
            }
        }

        [Fact]
        public async Task RunAsync_UpToDate_DownloadsNothing()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.Publish(Running);

                Assert.True(await h.SelfUpdateAsync());

                Assert.Empty(h.Releases.Downloads);
                Assert.Contains("updaemon is up to date (1.0.0).", h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_LatestIsOlderThanTheRunningOne_DownloadsNothing()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.Publish(UpdaemonVersion.Of(0, 9, 0));

                Assert.True(await h.SelfUpdateAsync());

                Assert.Empty(h.Releases.Downloads);
                Assert.Contains("updaemon is up to date (1.0.0).", h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_LatestReleaseHasNoVersionTag_Fails()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.Latest = null;

                Assert.False(await h.SelfUpdateAsync());

                Assert.Equal(new[] { "Error: updaemon: could not read its latest release: its tag is not v<x.y.z>" }, h.Output.Errors);
            }
        }

        [Fact]
        public async Task RunAsync_TurnedOff_DoesNothing()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                UpdaemonConfig config = await h.Config.LoadConfigAsync();
                config.SelfUpdate = false;
                await h.Config.SaveConfigAsync(config);
                h.Releases.Publish(Next);

                Assert.True(await h.SelfUpdateAsync());

                Assert.Empty(h.Releases.Downloads);
                Assert.Empty(h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_RunningAnotherUpdaemon_SkipsNamingWhatItWouldReplace()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                string other = Path.Combine(h.Scope.RootDirectory, "other-updaemon");
                File.WriteAllText(other, "");
                h.Executable = new RunningExecutable(other);
                h.Releases.Publish(Next);

                Assert.True(await h.SelfUpdateAsync());

                Assert.Empty(h.Releases.Downloads);
                Assert.Equal(
                    new[] { $"Self-update skipped: it replaces {h.Scope.UpdaemonCommandPath}, and this updaemon is {other}." },
                    h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_UpdaemonInstalledAsAPlainFile_ReplacesItWithTheLink()
        {
            using (AppHarness h = new AppHarness())
            {
                // As 0.x's install.sh left it.
                Directory.CreateDirectory(h.Scope.BinDirectory);
                File.WriteAllText(h.Scope.UpdaemonCommandPath, "#!/bin/sh\necho 0.10.0\n");
                h.Executable = new RunningExecutable(h.Scope.UpdaemonCommandPath);
                h.Releases.Publish(Next);

                Assert.True(await h.SelfUpdateAsync());

                Assert.Equal(h.Scope.GetUpdaemonBinaryPath(Next), LinkTarget(h));
            }
        }

        [Fact]
        public async Task RunAsync_UrlPlugin_IsNamedAsNotUpdated()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                await h.Config.AddOrUpdatePluginAsync(PluginRecords.FromUrl("mine", "/p"));
                h.Releases.Publish(Next);

                await h.SelfUpdateAsync();

                Assert.Contains(h.Output.Messages, m => m.StartsWith("The 'mine' plugin was installed from a URL, so it is not updated with updaemon."));
            }
        }

        [Fact]
        public async Task RunAsync_GitHubUnreachable_FailsAndKeepsTheOldOne()
        {
            using (AppHarness h = ScopeRunningItsOwnUpdaemon())
            {
                h.Releases.LatestFailure = new HttpRequestException("no network");

                Assert.False(await h.SelfUpdateAsync());

                Assert.Equal(h.Scope.GetUpdaemonBinaryPath(Running), LinkTarget(h));
                Assert.Equal(new[] { "Error: updaemon: could not read its latest release: no network" }, h.Output.Errors);
            }
        }
    }
}
