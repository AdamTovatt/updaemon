using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;
using static Updaemon.Tests.Helpers.ShelfPackages;

namespace Updaemon.Tests.Configuration
{
    /// <summary>The one way to begin a change to an installed app.</summary>
    public class PreparedAppTests
    {
        [Fact]
        public async Task BeginChangeAsync_AppRemovedSince_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                PreparedApp prepared = await h.Preparer.PrepareAsync(await h.HostAsync());
                await h.Config.RemoveAppAsync("shelf");

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.BeginChangeAsync());

                Assert.Equal("shelf was changed since it was prepared; prepare it again.", ex.Message);
            }
        }

        /// <summary>Editing the copy is not seen, nor saved, until it is committed.</summary>
        [Fact]
        public async Task BeginChangeAsync_HandsOutACopyOfItsOwn()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                Assert.Equal(0, await h.Set().ExecuteAsync(new[] { "shelf", "A=1" }));
                PreparedApp prepared = await h.Preparer.PrepareAsync(await h.HostAsync());
                IReadOnlyList<string> before = h.Snapshot();

                AppChange change = await prepared.BeginChangeAsync();
                Assert.True(change.After.IsSameAs(change.Before));
                change.After.Settings["A"] = "2";
                change.After.FindExtension("images")!.RecordSwitch(from: Two, to: One);

                Assert.Equal("1", prepared.App.Settings["A"]);
                Assert.Empty(prepared.App.FindExtension("images")!.SkippedVersions);
                h.AssertUnchangedSince(before);
            }
        }
    }
}
