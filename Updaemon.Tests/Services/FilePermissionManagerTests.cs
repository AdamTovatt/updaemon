using Updaemon.IO;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    public class FilePermissionManagerTests
    {
        [Fact]
        public async Task SetExecutablePermissionsAsync_MakesItExecutableSilently()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockOutputWriter outputWriter = new MockOutputWriter();
                string path = tempHelper.CreateTempFile("app", "x");
                File.SetUnixFileMode(path, AtomicFile.OwnerOnly);

                await new FilePermissionManager(outputWriter).SetExecutablePermissionsAsync(path);

                Assert.Equal(AtomicFile.Executable, File.GetUnixFileMode(path));
                Assert.Empty(outputWriter.Messages);
                Assert.Empty(outputWriter.Errors);
            }
        }

        [Fact]
        public async Task MakeTreeReadableAsync_MakesItReadableSilently()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockOutputWriter outputWriter = new MockOutputWriter();
                string file = tempHelper.CreateTempFile("dir/file", "x");
                File.SetUnixFileMode(file, AtomicFile.OwnerOnly);

                await new FilePermissionManager(outputWriter).MakeTreeReadableAsync(tempHelper.GetFullPath("dir"));

                Assert.Equal(AtomicFile.Readable, File.GetUnixFileMode(file));
                Assert.Empty(outputWriter.Messages);
                Assert.Empty(outputWriter.Errors);
            }
        }

        [Fact]
        public async Task MakeDirectoryEnterableAsync_LeavesWhatIsInIt()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockOutputWriter outputWriter = new MockOutputWriter();
                string file = tempHelper.CreateTempFile("dir/file", "x");
                string directory = tempHelper.GetFullPath("dir");
                File.SetUnixFileMode(file, AtomicFile.OwnerOnly);
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

                await new FilePermissionManager(outputWriter).MakeDirectoryEnterableAsync(directory);

                Assert.Equal(AtomicFile.Executable, File.GetUnixFileMode(directory));
                Assert.Equal(AtomicFile.OwnerOnly, File.GetUnixFileMode(file));
                Assert.Empty(outputWriter.Errors);
            }
        }

        [Fact]
        public async Task SetExecutablePermissionsAsync_MissingFile_WarnsWithoutThrowing()
        {
            MockOutputWriter outputWriter = new MockOutputWriter();

            await new FilePermissionManager(outputWriter).SetExecutablePermissionsAsync("/nonexistent/updaemon-test-executable");

            Assert.StartsWith("Warning: Could not set executable permissions on /nonexistent/updaemon-test-executable", Assert.Single(outputWriter.Errors));
        }
    }
}
