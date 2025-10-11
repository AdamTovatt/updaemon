using System.IO.Compression;
using Updaemon.Services;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
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

        [Fact]
        public async Task ProcessAsync_CorruptedZipFile_DoesNotThrow()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string targetDirectory = tempHelper.CreateTempDirectory("corrupted");
                tempHelper.CreateTempFile("corrupted/app.zip", "not a valid zip file");

                DownloadPostProcessor processor = new DownloadPostProcessor();

                await processor.ProcessAsync(targetDirectory);

                // Should not throw, and the corrupted file should still exist
                Assert.True(File.Exists(Path.Combine(targetDirectory, "app.zip")));
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

