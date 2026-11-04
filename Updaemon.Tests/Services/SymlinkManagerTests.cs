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
    }
}
