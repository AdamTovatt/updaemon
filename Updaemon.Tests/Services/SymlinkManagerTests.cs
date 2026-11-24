using Updaemon.Services;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    public class SymlinkManagerTests
    {
        [Fact]
        public async Task CreateOrUpdateSymlinkAsync_PathIsARealDirectory_RefusesAndLeavesIt()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                string directory = temp.CreateTempDirectory("current");
                string target = temp.CreateTempDirectory("1.0.0");

                await Assert.ThrowsAsync<InvalidOperationException>(() => new SymlinkManager().CreateOrUpdateSymlinkAsync(directory, target));

                Assert.Null(File.ResolveLinkTarget(directory, returnFinalTarget: false));
                Assert.Equal(new[] { directory, target }.Order(), Directory.GetFileSystemEntries(temp.TempDirectory).Order());
            }
        }

        [Fact]
        public async Task CreateOrUpdateSymlinkAsync_CannotBeRenamed_LeavesNothingBehind()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                string target = temp.CreateTempDirectory("1.0.0");
                string temporaryDirectory = temp.CreateTempDirectory("versions");
                string link = temp.GetFullPath("missing/current");

                IOException ex = await Assert.ThrowsAsync<IOException>(() => new SymlinkManager().CreateOrUpdateSymlinkAsync(link, target, temporaryDirectory));

                Assert.StartsWith($"Could not point '{link}' at '{target}': ", ex.Message);
                Assert.Empty(Directory.GetFileSystemEntries(temporaryDirectory));
            }
        }
    }
}
