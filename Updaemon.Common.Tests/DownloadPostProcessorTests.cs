using System.Formats.Tar;
using System.IO.Compression;
using Updaemon.Common.Tests.Helpers;
using Updaemon.Common.Utilities;

namespace Updaemon.Common.Tests
{
    public class DownloadPostProcessorTests
    {
        [Fact]
        public async Task ProcessAsync_NonExistentDirectory_DoesNotThrow()
        {
            DownloadPostProcessor processor = new DownloadPostProcessor();

            await processor.ProcessAsync("/non/existent/path");

            // Should complete without throwing
        }

        [Fact]
        public async Task ProcessAsync_EmptyDirectory_DoesNothing()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("empty");
                DownloadPostProcessor processor = new DownloadPostProcessor();

                await processor.ProcessAsync(targetDirectory);

                Assert.True(Directory.Exists(targetDirectory));
                Assert.Empty(Directory.GetFileSystemEntries(targetDirectory));
            }
        }

        [Fact]
        public async Task ProcessAsync_MultipleFiles_DoesNothing()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("multi");
                tempHelper.CreateTempFile("multi/file1.txt", "content1");
                tempHelper.CreateTempFile("multi/file2.txt", "content2");
                DownloadPostProcessor processor = new DownloadPostProcessor();

                await processor.ProcessAsync(targetDirectory);

                Assert.Equal(2, Directory.GetFiles(targetDirectory).Length);
            }
        }

        [Fact]
        public async Task ProcessAsync_SingleNonArchiveFile_DoesNothing()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("single");
                string filePath = tempHelper.CreateTempFile("single/app.exe", "content");
                DownloadPostProcessor processor = new DownloadPostProcessor();

                await processor.ProcessAsync(targetDirectory);

                Assert.True(File.Exists(filePath));
                Assert.Single(Directory.GetFiles(targetDirectory));
            }
        }

        [Fact]
        public async Task ProcessAsync_SingleZipFile_ExtractsAndDeletesZip()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("zip");
                string zipPath = Path.Combine(targetDirectory, "app.zip");

                // Create a zip file with some content
                string sourceDirectory = tempHelper.CreateTempDirectory("source");
                tempHelper.CreateTempFile("source/app", "executable content");
                tempHelper.CreateTempFile("source/config.json", "{}");
                ZipFile.CreateFromDirectory(sourceDirectory, zipPath);

                DownloadPostProcessor processor = new DownloadPostProcessor();

                await processor.ProcessAsync(targetDirectory);

                // Zip should be deleted
                Assert.False(File.Exists(zipPath));

                // Contents should be extracted
                Assert.True(File.Exists(Path.Combine(targetDirectory, "app")));
                Assert.True(File.Exists(Path.Combine(targetDirectory, "config.json")));
            }
        }

        [Fact]
        public async Task ProcessAsync_ZipWithSingleDirectory_ExtractsAndUnwraps()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("zip-unwrap");
                string zipPath = Path.Combine(targetDirectory, "app.zip");

                // Create a zip file with a single directory containing files
                string sourceDirectory = tempHelper.CreateTempDirectory("source");
                string nestedDirectory = tempHelper.CreateTempDirectory("source/app-1.0.0");
                tempHelper.CreateTempFile("source/app-1.0.0/app", "executable content");
                tempHelper.CreateTempFile("source/app-1.0.0/lib.dll", "library");
                ZipFile.CreateFromDirectory(sourceDirectory, zipPath);

                DownloadPostProcessor processor = new DownloadPostProcessor();

                await processor.ProcessAsync(targetDirectory);

                // Zip should be deleted
                Assert.False(File.Exists(zipPath));

                // Nested directory should be unwrapped (files moved up)
                Assert.True(File.Exists(Path.Combine(targetDirectory, "app")));
                Assert.True(File.Exists(Path.Combine(targetDirectory, "lib.dll")));

                // The nested directory should be removed
                Assert.False(Directory.Exists(Path.Combine(targetDirectory, "app-1.0.0")));
            }
        }

        [Fact]
        public async Task ProcessAsync_ZipWithMultipleTopLevelItems_ExtractsWithoutUnwrapping()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("zip-multi");
                string zipPath = Path.Combine(targetDirectory, "app.zip");

                // Create a zip file with multiple top-level items
                string sourceDirectory = tempHelper.CreateTempDirectory("source");
                tempHelper.CreateTempFile("source/app", "executable content");
                tempHelper.CreateTempFile("source/readme.md", "documentation");
                ZipFile.CreateFromDirectory(sourceDirectory, zipPath);

                DownloadPostProcessor processor = new DownloadPostProcessor();

                await processor.ProcessAsync(targetDirectory);

                // Files should be extracted as-is (no unwrapping)
                Assert.True(File.Exists(Path.Combine(targetDirectory, "app")));
                Assert.True(File.Exists(Path.Combine(targetDirectory, "readme.md")));
            }
        }

        [Theory]
        [InlineData("app.zip")]
        [InlineData("app.tar.gz")]
        public async Task ProcessAsync_CorruptArchive_FailsNamingIt(string archiveName)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("corrupted");
                tempHelper.CreateTempFile($"corrupted/{archiveName}", "not a valid archive");

                InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new DownloadPostProcessor().ProcessAsync(targetDirectory));

                Assert.StartsWith($"{archiveName} could not be extracted: ", exception.Message);
            }
        }

        [Fact]
        public async Task ProcessAsync_TarGzWithSingleDirectory_ExtractsKeepingModesAndUnwraps()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("targz");
                WriteTarGz(Path.Combine(targetDirectory, "app-linux-arm64.tar.gz"), writer =>
                {
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "app-1.0.0/"));
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "app-1.0.0/app") { Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute, DataStream = new MemoryStream("#!/bin/sh\n"u8.ToArray()) });
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "app-1.0.0/readme.md") { Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, DataStream = new MemoryStream("hi"u8.ToArray()) });
                });

                await new DownloadPostProcessor().ProcessAsync(targetDirectory);

                Assert.Equal(new[] { "app", "readme.md" }, Directory.GetFileSystemEntries(targetDirectory).Select(Path.GetFileName).Order());
                Assert.True(File.GetUnixFileMode(Path.Combine(targetDirectory, "app")).HasFlag(UnixFileMode.UserExecute));
                Assert.False(File.GetUnixFileMode(Path.Combine(targetDirectory, "readme.md")).HasFlag(UnixFileMode.UserExecute));
            }
        }

        [Fact]
        public async Task ProcessAsync_SingleDirectoryHoldingAnEntryOfItsOwnName_Unwraps()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("same-name");
                string sourceDirectory = tempHelper.CreateTempDirectory("source");
                tempHelper.CreateTempFile("source/app/app", "executable content");
                ZipFile.CreateFromDirectory(sourceDirectory, Path.Combine(targetDirectory, "app.zip"));

                await new DownloadPostProcessor().ProcessAsync(targetDirectory);

                Assert.Equal("executable content", File.ReadAllText(Path.Combine(targetDirectory, "app")));
                Assert.Single(Directory.GetFileSystemEntries(targetDirectory));
            }
        }

        [Fact]
        public async Task ProcessAsync_ZipEntryOutsideTheTarget_IsRefused()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("zip-escape");
                using (ZipArchive zip = ZipFile.Open(Path.Combine(targetDirectory, "app.zip"), ZipArchiveMode.Create))
                using (StreamWriter entry = new StreamWriter(zip.CreateEntry("../escaped").Open()))
                {
                    entry.Write("outside");
                }

                await Assert.ThrowsAsync<InvalidOperationException>(() => new DownloadPostProcessor().ProcessAsync(targetDirectory));

                Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(targetDirectory)!, "escaped")));
            }
        }

        [Fact]
        public async Task ProcessAsync_TarEntryOutsideTheTarget_IsRefused()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("tar-escape");
                WriteTarGz(Path.Combine(targetDirectory, "app.tar.gz"), writer =>
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "../escaped") { DataStream = new MemoryStream("outside"u8.ToArray()) }));

                await Assert.ThrowsAsync<InvalidOperationException>(() => new DownloadPostProcessor().ProcessAsync(targetDirectory));

                Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(targetDirectory)!, "escaped")));
            }
        }

        /// <summary>Links named "link" whose target lies outside the extraction directory.</summary>
        public static TheoryData<TarEntry> EscapingTarLinks => new TheoryData<TarEntry>
        {
            new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "../outside" },
            new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "/etc/passwd" },
            new PaxTarEntry(TarEntryType.HardLink, "link") { LinkName = "../outside" },
        };

        [Theory]
        [MemberData(nameof(EscapingTarLinks))]
        public async Task ProcessAsync_TarLinkOutsideTheTarget_IsRefused(TarEntry escaping)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                // A real file outside, so a link to it could be made if extraction allowed it.
                tempHelper.CreateTempFile("outside", "secret");
                string targetDirectory = tempHelper.CreateTempDirectory("tar-escape");
                WriteTarGz(Path.Combine(targetDirectory, "app.tar.gz"), writer => writer.WriteEntry(escaping));

                await Assert.ThrowsAsync<InvalidOperationException>(() => new DownloadPostProcessor().ProcessAsync(targetDirectory));

                Assert.DoesNotContain("link", Directory.GetFileSystemEntries(targetDirectory).Select(Path.GetFileName));
            }
        }

        [Fact]
        public async Task ProcessAsync_TarGzWithSymlinksInItsSingleDirectory_UnwrapsKeepingTheLinks()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("targz-links");
                WriteTarGz(Path.Combine(targetDirectory, "app-linux-arm64.tar.gz"), writer =>
                {
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "app/"));
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "app/bin/"));
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "app/bin/tool") { DataStream = new MemoryStream("tool"u8.ToArray()) });
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "app/current") { LinkName = "bin" });
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "app/run") { LinkName = "bin/tool" });

                    // Whatever order the entries move in, one link's target is missing and one's is a directory.
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "app/pending") { LinkName = "made-at-first-run" });
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "app/here") { LinkName = "." });
                });

                await new DownloadPostProcessor().ProcessAsync(targetDirectory);

                Assert.Equal(new[] { "bin", "current", "here", "pending", "run" }, Directory.GetFileSystemEntries(targetDirectory).Select(Path.GetFileName).Order());
                Assert.Equal("made-at-first-run", new FileInfo(Path.Combine(targetDirectory, "pending")).LinkTarget);
                Assert.Equal(".", new FileInfo(Path.Combine(targetDirectory, "here")).LinkTarget);
                Assert.Equal("bin", new DirectoryInfo(Path.Combine(targetDirectory, "current")).LinkTarget);
                Assert.Equal("bin/tool", new FileInfo(Path.Combine(targetDirectory, "run")).LinkTarget);
                Assert.Equal("tool", File.ReadAllText(Path.Combine(targetDirectory, "current", "tool")));
                Assert.Equal("tool", File.ReadAllText(Path.Combine(targetDirectory, "run")));
            }
        }

        private static void WriteTarGz(string path, Action<TarWriter> write)
        {
            using (FileStream file = File.Create(path))
            using (GZipStream gzip = new GZipStream(file, CompressionLevel.Fastest))
            using (TarWriter writer = new TarWriter(gzip, TarEntryFormat.Pax))
            {
                write(writer);
            }
        }

        [Fact]
        public async Task ProcessAsync_ZipWithNestedDirectories_OnlyUnwrapsSingleTopLevel()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("zip-nested");
                string zipPath = Path.Combine(targetDirectory, "app.zip");

                // Create a zip with single top-level dir containing nested structure
                string sourceDirectory = tempHelper.CreateTempDirectory("source");
                string appDirectory = tempHelper.CreateTempDirectory("source/app");
                string binDirectory = tempHelper.CreateTempDirectory("source/app/bin");
                tempHelper.CreateTempFile("source/app/bin/app", "executable");
                tempHelper.CreateTempFile("source/app/config.json", "{}");
                ZipFile.CreateFromDirectory(sourceDirectory, zipPath);

                DownloadPostProcessor processor = new DownloadPostProcessor();

                await processor.ProcessAsync(targetDirectory);

                // Should unwrap the top "app" directory but keep "bin" as a subdirectory
                Assert.True(Directory.Exists(Path.Combine(targetDirectory, "bin")));
                Assert.True(File.Exists(Path.Combine(targetDirectory, "bin", "app")));
                Assert.True(File.Exists(Path.Combine(targetDirectory, "config.json")));
            }
        }

        [Fact]
        public async Task ProcessAsync_SingleDirectoryWithoutArchive_DoesNotUnwrap()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("single-dir");
                string nestedDirectory = tempHelper.CreateTempDirectory("single-dir/app-folder");
                tempHelper.CreateTempFile("single-dir/app-folder/app", "content");

                DownloadPostProcessor processor = new DownloadPostProcessor();

                await processor.ProcessAsync(targetDirectory);

                // Should not unwrap since there was no archive extraction
                Assert.True(Directory.Exists(nestedDirectory));
                Assert.True(File.Exists(Path.Combine(nestedDirectory, "app")));
            }
        }
    }
}

