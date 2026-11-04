using Updaemon.IO;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.IO
{
    public class AtomicFileTests
    {
        [SkippableFact]
        public async Task WriteAsync_SameContentAndMode_SucceedsInAnUnwritableDirectory()
        {
            Unwritable.SkipUnderRoot();
            using (TempFileHelper temp = new TempFileHelper())
            {
                string path = temp.GetFullPath("app.env");
                await AtomicFile.WriteAsync(path, "A=1\n", AtomicFile.Readable);

                await Unwritable.WhileAsync(temp.TempDirectory, async () =>
                {
                    await AtomicFile.WriteAsync(path, "A=1\n", AtomicFile.Readable);
                    return true;
                });

                Assert.Equal("A=1\n", File.ReadAllText(path));
            }
        }

        public static TheoryData<string, UnixFileMode> Changes => new TheoryData<string, UnixFileMode>
        {
            { "A=2\n", AtomicFile.Readable },
            { "A=1\n", AtomicFile.OwnerOnly },
        };

        [SkippableTheory]
        [MemberData(nameof(Changes))]
        public async Task WriteAsync_OtherContentOrMode_IsWrittenAndNamesThePathWhenItCannotBe(string content, UnixFileMode mode)
        {
            Unwritable.SkipUnderRoot();
            using (TempFileHelper temp = new TempFileHelper())
            {
                string path = temp.GetFullPath("app.env");
                await AtomicFile.WriteAsync(path, "A=1\n", AtomicFile.Readable);

                IOException ex = await Assert.ThrowsAsync<IOException>(
                    () => Unwritable.WhileAsync(temp.TempDirectory, async () =>
                    {
                        await AtomicFile.WriteAsync(path, content, mode);
                        return true;
                    }));
                await AtomicFile.WriteAsync(path, content, mode);

                Assert.StartsWith($"Could not write {path}: ", ex.Message);
                Assert.Equal(content, File.ReadAllText(path));
                Assert.Equal(mode, File.GetUnixFileMode(path));
            }
        }
    }
}
