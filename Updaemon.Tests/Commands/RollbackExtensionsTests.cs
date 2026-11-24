using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using static Updaemon.Tests.Helpers.ShelfPackages;

namespace Updaemon.Tests.Commands
{
    /// <summary>rollback of an extension, and of a host with extensions.</summary>
    public class RollbackExtensionsTests
    {
        private static Task<int> RollBackAsync(AppHarness h, params string[] args) => h.RollbackCommand().ExecuteAsync(args);

        /// <summary>The host and its extension images, both updated from 1.0.0 to 2.0.0.</summary>
        private static async Task<AppHarness> UpdatedAsync(bool isSystem = false)
        {
            AppHarness h = new AppHarness(isSystem);
            await h.InstallHostWithImagesAsync();
            h.AddHostRelease("2.0.0");
            h.AddExtensionRelease(ImagesRemote, "2.0.0");
            Assert.Equal(0, await h.Update().ExecuteAsync(Array.Empty<string>()));
            h.ForgetCalls();
            return h;
        }

        [Theory]
        [InlineData(false, "shelf/images")]
        [InlineData(true, "shelf/images")]
        [InlineData(false, "shelf/images", "1.0")]
        public async Task Rollback_Extension_AsksTheHostLinksItSkipsTheNewerAndRestarts(bool isSystem, params string[] args)
        {
            using (AppHarness h = await UpdatedAsync(isSystem))
            {
                Assert.Equal(0, await RollBackAsync(h, args));

                Assert.Equal(new[] { "Rolled back shelf/images from 2.0.0 to 1.0.0. 'updaemon update' skips 2.0.0 from now on." }, h.Output.Messages);
                Assert.Equal(new[] { "images -> 1.0.0" }, h.LinkedExtensions());
                Assert.Equal(Two, await h.Versions.GetCurrentAsync(Shelf));
                ProcessInvocation check = Assert.Single(h.Checks);
                Assert.Equal(h.Versions.GetVersionDirectory(Shelf, Two), check.WorkingDirectory);
                Assert.Equal(h.Versions.GetVersionDirectory(Images, One), check.Arguments[^1]);
                Assert.Equal(isSystem ? "daemon" : null, check.User);
                Assert.Equal(1, h.HostRestarts());
                InstalledApp host = await h.HostAsync();
                Assert.Equal(new[] { Two }, host.FindExtension("images")!.SkippedVersions);
                Assert.Empty(host.SkippedVersions);
                Assert.Null(host.Unfinished);
            }
        }

        [Theory]
        [InlineData(false, 0, "1.0.0")]
        [InlineData(true, 1, "2.0.0")]
        public async Task Rollback_Extension_PassesOverItsOwnSkippedVersionsNotItsHosts(bool ownIsSkipped, int exitCode, string current)
        {
            using (AppHarness h = await UpdatedAsync())
            {
                InstalledApp host = await h.HostAsync();
                InstalledPackage skips = ownIsSkipped ? host.FindExtension("images")! : host;
                skips.RecordSwitch(from: One, to: new Version(0, 9));
                await h.Config.SaveAppAsync(host);

                Assert.Equal(exitCode, await RollBackAsync(h, "shelf/images"));

                Assert.Equal(new[] { $"images -> {current}" }, h.LinkedExtensions());
            }
        }

        [Fact]
        public async Task Rollback_ExtensionTheHostRefuses_StaysAndSaysWhy()
        {
            using (AppHarness h = await UpdatedAsync())
            {
                h.Refuse("wrong ABI");
                IReadOnlyList<string> before = h.Snapshot();

                Assert.Equal(1, await RollBackAsync(h, "shelf/images"));

                Assert.Equal(new[] { "Error: shelf/images was not rolled back: shelf 2.0.0 refuses 1.0.0: wrong ABI." }, h.Output.Errors);
                Assert.Equal(0, h.HostRestarts());
                h.AssertUnchangedSince(before);
            }
        }

        [Theory]
        [MemberData(nameof(HostsLeftRunning), MemberType = typeof(ShelfPackages))]
        public async Task Rollback_ExtensionOfAManualOrDisabledHost_LeavesItRunning(string message, Func<AppHarness, Task> leaveRunning)
        {
            using (AppHarness h = await UpdatedAsync())
            {
                await leaveRunning(h);

                Assert.Equal(0, await RollBackAsync(h, "shelf/images"));

                Assert.Equal(new[] { "images -> 1.0.0" }, h.LinkedExtensions());
                Assert.Contains(message, h.Output.Messages);
                Assert.Equal(0, h.HostRestarts());
            }
        }

        [Fact]
        public async Task Rollback_ExtensionAndTheHostDoesNotStayUp_StandsAndFails()
        {
            using (AppHarness h = await UpdatedAsync())
            {
                h.ServiceManager.CrashOnNextStart.Add("shelf");

                Assert.Equal(1, await RollBackAsync(h, "shelf/images"));

                Assert.Equal(new[] { "Error: shelf did not stay running after the restart. See 'updaemon logs shelf'." }, h.Output.Errors);
                Assert.Equal(new[] { "images -> 1.0.0" }, h.LinkedExtensions());
            }
        }

        [Fact]
        public async Task Rollback_ExtensionAndTheServiceManagerIsUnreachable_ChangesNothing()
        {
            using (AppHarness h = await UpdatedAsync())
            {
                h.ServiceManager.Unreachable = "no user manager";
                IReadOnlyList<string> before = h.Snapshot();

                await Assert.ThrowsAsync<ServiceManagerUnreachableException>(() => RollBackAsync(h, "shelf/images"));

                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Rollback_ExtensionCannotBeRecorded_PutsItBack()
        {
            using (AppHarness h = await UpdatedAsync())
            {
                IReadOnlyList<string> before = h.Snapshot();
                h.SavedConfig.FailNext(app => app.Unfinished is not UnfinishedChange.PutBack);

                await Assert.ThrowsAsync<IOException>(() => RollBackAsync(h, "shelf/images"));

                Assert.Equal(0, h.HostRestarts());
                h.AssertUnchangedSince(before);
            }
        }

        [Theory]
        [InlineData("Error: No version of shelf/images older than 1.0.0 is kept, apart from skipped ones. Name one to roll back to it anyway.", "shelf/images")]
        [InlineData("Error: shelf/images 1.0.0 is already the current version.", "shelf/images", "1.0.0")]
        [InlineData("Error: 'shelf/video' is not installed.", "shelf/video")]
        public async Task Rollback_OfWhatCannotBeRolledBack_Fails(string error, params string[] args)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();

                Assert.Equal(1, await RollBackAsync(h, args));

                Assert.Equal(new[] { error }, h.Output.Errors);
            }
        }

        [Fact]
        public async Task Rollback_ExtensionToAVersionNotKept_ThrowsListingTheKept()
        {
            using (AppHarness h = await UpdatedAsync())
            {
                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RollBackAsync(h, "shelf/images", "9.0.0"));

                Assert.Equal("shelf/images 9.0.0 is not kept. Kept versions: 1.0.0, 2.0.0.", ex.Message);
            }
        }

        [Fact]
        public async Task Rollback_ExtensionOfAKilledInstall_TakesItOutAndSaysItIsNotInstalled()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                await h.KilledInstallOfImagesAsync();

                Assert.Equal(1, await RollBackAsync(h, "shelf/images"));

                Assert.Equal(new[] { "Error: 'shelf/images' is not installed." }, h.Output.Errors);
            }
        }

        [Fact]
        public async Task Rollback_Host_LeavesItsExtensionsAndAsksTheVersionRolledBackTo()
        {
            using (AppHarness h = await UpdatedAsync())
            {
                Assert.Equal(0, await RollBackAsync(h, "shelf"));

                Assert.Empty(h.Output.Errors);
                Assert.Equal(One, await h.Versions.GetCurrentAsync(Shelf));
                Assert.Equal(new[] { "images -> 2.0.0" }, h.LinkedExtensions());
                Assert.Equal(h.Versions.GetVersionDirectory(Shelf, One), Assert.Single(h.Checks).WorkingDirectory);
                Assert.Equal(1, h.HostRestarts());
            }
        }

        [Fact]
        public async Task Rollback_HostThatRefusesAnExtension_GoesAheadAndNamesIt()
        {
            using (AppHarness h = await UpdatedAsync())
            {
                h.Refuse("wrong ABI");

                Assert.Equal(0, await RollBackAsync(h, "shelf"));

                Assert.Equal(new[] { "Warning: shelf/images: shelf 1.0.0 refuses 2.0.0: wrong ABI. shelf runs without it." }, h.Output.Errors);
                Assert.Equal(One, await h.Versions.GetCurrentAsync(Shelf));
                Assert.Equal(new[] { "images -> 2.0.0" }, h.LinkedExtensions());
            }
        }

        [Fact]
        public async Task Rollback_HostWithABrokenExtension_GoesAheadAndWarns()
        {
            using (AppHarness h = await UpdatedAsync())
            {
                h.Versions.Unlink(Images);

                Assert.Equal(0, await RollBackAsync(h, "shelf"));

                Assert.StartsWith("Warning: shelf/images: shelf/images has no current version.", Assert.Single(h.Output.Errors));
                Assert.Equal(One, await h.Versions.GetCurrentAsync(Shelf));
            }
        }

        /// <summary>Nothing is asked of a version that cannot take over.</summary>
        [Fact]
        public async Task Rollback_HostThatCannotBe_AsksNothing()
        {
            using (AppHarness h = await UpdatedAsync())
            {
                File.Delete(Path.Combine(h.Versions.GetVersionDirectory(Shelf, One), "bin", "shelf"));

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RollBackAsync(h, "shelf"));

                Assert.Equal("shelf 1.0.0: the executable 'bin/shelf' named in updaemon.json is not in the release.", ex.Message);
                Assert.Empty(h.Checks);
            }
        }
    }
}
