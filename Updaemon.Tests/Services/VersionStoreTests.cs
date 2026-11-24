using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    public class VersionStoreTests
    {
        private static readonly ExtensionName Images = new ExtensionName("shelf", "images");

        private static Task<Version> DownloadAsync(AppHarness h, string version) => h.Versions.DownloadAsync(
            Images, new InstalledExtension { Id = "images", Remote = "owner/images" }, Version.Parse(version), h.Client, _ => Task.FromResult(Version.Parse(version)));

        /// <summary>A run killed while downloading must not leave what a rollback could take for a version.</summary>
        [Fact]
        public async Task DownloadAsync_IsAVersionOnlyOnceComplete()
        {
            using (AppHarness h = new AppHarness())
            {
                h.AddExtensionRelease("owner/images", "1.0.0");
                IReadOnlyList<Version>? whileDownloading = null;
                h.AfterDownloading("owner/images", "1.0.0", _ =>
                {
                    whileDownloading = h.Versions.GetRetained(Images);
                    h.KeepForCutOff();
                });

                await DownloadAsync(h, "1.0.0");
                Assert.Empty(whileDownloading!);
                Assert.Equal(new[] { new Version(1, 0, 0) }, h.Versions.GetRetained(Images));

                h.CutOff();
                Assert.Empty(h.Versions.GetRetained(Images));
                h.AddExtensionRelease("owner/images", "2.0.0");
                await DownloadAsync(h, "2.0.0");
                Assert.Equal(new[] { "2.0.0" }, Directory.GetFileSystemEntries(h.Scope.Locate(Images).VersionsDirectory).Select(Path.GetFileName));
            }
        }

        [Fact]
        public async Task DownloadAsync_ReadingItFails_LeavesNothing()
        {
            using (AppHarness h = new AppHarness())
            {
                h.AddExtensionRelease("owner/images", "1.0.0");

                await Assert.ThrowsAsync<InvalidOperationException>(() => h.Versions.DownloadAsync<Version>(
                    Images, new InstalledExtension { Remote = "owner/images" }, new Version(1, 0, 0), h.Client, _ => throw new InvalidOperationException("no")));

                Assert.False(Directory.Exists(h.Scope.GetAppDirectory("shelf")));
            }
        }

        [Fact]
        public async Task PruneAsync_AnExtension_KeepsItsNewestAndCurrent_AndTheHostsVersions()
        {
            using (AppHarness h = new AppHarness())
            {
                foreach (string version in new[] { "1.0.0", "1.1.0", "1.2.0", "1.3.0" })
                {
                    Directory.CreateDirectory(h.Versions.GetVersionDirectory(Images, Version.Parse(version)));
                    Directory.CreateDirectory(h.Versions.GetVersionDirectory(new AppName("shelf"), Version.Parse(version)));
                }

                Directory.CreateDirectory(h.Scope.GetExtensionsDirectory("shelf"));
                await h.Versions.PointAsync(Images, new Version(1, 0, 0));

                await h.Versions.PruneAsync(Images, 2);

                Assert.Equal(new[] { "1.0.0", "1.2.0", "1.3.0" }, h.Versions.GetRetained(Images).Select(version => version.ToString()));
                Assert.Equal(4, h.Versions.GetRetained(new AppName("shelf")).Count);
            }
        }

        [Fact]
        public async Task PointAsync_VersionNotKept_ThrowsAndLeavesTheLink()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();

                await Assert.ThrowsAsync<InvalidOperationException>(() => h.Versions.PointAsync(Images, new Version(9, 0, 0)));

                Assert.Equal(new[] { "images -> 1.0.0" }, h.LinkedExtensions());
            }
        }

        [Fact]
        public async Task DiscardAsync_VersionLeftCurrentByAFailedSwitchBack_IsKept()
        {
            using (AppHarness h = new AppHarness())
            {
                h.AddExtensionRelease("owner/images", "1.0.0");
                Version version = await DownloadAsync(h, "1.0.0");
                Directory.CreateDirectory(h.Scope.GetExtensionsDirectory("shelf"));
                await h.Versions.PointAsync(Images, version);

                await h.Versions.DiscardAsync(Images, version);

                Assert.Equal(new[] { version }, h.Versions.GetRetained(Images));
            }
        }

        [Fact]
        public async Task DiscardAsync_DownloadedVersion_IsDeletedAndTheCurrentOneKept()
        {
            using (AppHarness h = new AppHarness())
            {
                h.AddExtensionRelease("owner/images", "1.0.0");
                Version current = await DownloadAsync(h, "1.0.0");
                Directory.CreateDirectory(h.Scope.GetExtensionsDirectory("shelf"));
                await h.Versions.PointAsync(Images, current);
                h.AddExtensionRelease("owner/images", "2.0.0");
                Version downloaded = await DownloadAsync(h, "2.0.0");

                await h.Versions.DiscardAsync(Images, downloaded);

                Assert.Equal(new[] { current }, h.Versions.GetRetained(Images));
                Assert.Equal(current, await h.Versions.GetCurrentAsync(Images));
            }
        }

        /// <summary>Who runs the app, or its check, need not be who installed it.</summary>
        [Fact]
        public async Task DownloadAsync_MakesTheFilesReadableByEveryone()
        {
            using (AppHarness h = new AppHarness())
            {
                h.AddExtensionRelease("owner/images", "1.0.0");
                h.AfterDownloading("owner/images", "1.0.0", directory =>
                {
                    File.SetUnixFileMode(Path.Combine(directory, "plugin.dll"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                });
                VersionStore versions = new VersionStore(h.Scope, new SymlinkManager(), new FilePermissionManager(h.Output), h.Output);

                await versions.DownloadAsync(Images, new InstalledExtension { Remote = "owner/images" }, new Version(1, 0, 0), h.Client, _ => Task.FromResult(true));

                string directory = versions.GetVersionDirectory(Images, new Version(1, 0, 0));
                Assert.True(File.GetUnixFileMode(directory).HasFlag(UnixFileMode.OtherRead | UnixFileMode.OtherExecute));
                Assert.True(File.GetUnixFileMode(Path.Combine(directory, "plugin.dll")).HasFlag(UnixFileMode.OtherRead));
            }
        }

        [Fact]
        public async Task PruneAsync_KeepsNewestAndCurrent_IgnoresOtherDirectories()
        {
            using (AppHarness h = new AppHarness())
            {
                string appDirectory = h.Scope.GetAppDirectory("my-api");
                foreach (string version in new[] { "1.0.0", "1.1.0", "1.2.0", "1.3.0", "not-a-version" })
                {
                    Directory.CreateDirectory(Path.Combine(appDirectory, version));
                }

                File.CreateSymbolicLink(h.Scope.GetCurrentLinkPath("my-api"), Path.Combine(appDirectory, "1.0.0"));

                await h.Versions.PruneAsync(new AppName("my-api"), 2);

                string[] remaining = Directory.GetDirectories(appDirectory).Select(Path.GetFileName).Order().ToArray()!;
                Assert.Equal(new[] { "1.0.0", "1.2.0", "1.3.0", "current", "not-a-version" }, remaining);
            }
        }

        /// <summary>What the host loads is every folder in there: never one for an instant, or for good after a kill, that is no extension.</summary>
        [Fact]
        public async Task PointAsync_AnExtension_NothingButTheExtensionsIsEverInTheirFolder()
        {
            using (AppHarness h = new AppHarness())
            using (FileSystemWatcher watcher = new FileSystemWatcher())
            {
                await h.InstallHostWithImagesAsync();
                List<string> seen = new List<string>();
                watcher.Path = h.Scope.GetExtensionsDirectory("shelf");
                watcher.Created += (_, change) => { lock (seen) { seen.Add(change.Name!); } };
                watcher.Renamed += (_, change) => { lock (seen) { seen.Add(change.OldName!); seen.Add(change.Name!); } };
                watcher.EnableRaisingEvents = true;
                Directory.CreateDirectory(h.Versions.GetVersionDirectory(Images, new Version(2, 0, 0)));

                await h.Versions.PointAsync(Images, new Version(2, 0, 0));
                // Something to see for sure, after which the events before it have come too.
                Directory.CreateDirectory(Path.Combine(watcher.Path, "last"));
                for (int i = 0; i < 100 && !Seen(seen).Contains("last"); i++)
                {
                    await Task.Delay(50);
                }

                Assert.Empty(Seen(seen).Except(new[] { "images", "last" }));
            }
        }

        private static List<string> Seen(List<string> seen)
        {
            lock (seen)
            {
                return seen.ToList();
            }
        }
    }
}
