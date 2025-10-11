using Updaemon.Services;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    public class ExecutableDetectorTests
    {
        [Fact]
        public async Task FindExecutableAsync_NonExistentDirectory_ReturnsNull()
        {
            ExecutableDetector detector = new ExecutableDetector();

            string? result = await detector.FindExecutableAsync("/non/existent/path", "test-service");

            Assert.Null(result);
        }

        [Fact]
        public async Task FindExecutableAsync_WithUpdaemonJsonConfig_ReturnsConfiguredPath()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceDirectory = tempHelper.CreateTempDirectory("service");
                string binDirectory = tempHelper.CreateTempDirectory("service/bin");
                string executablePath = tempHelper.CreateTempFile("service/bin/my-app", "");
                tempHelper.CreateTempFile("service/updaemon.json", "{\"executablePath\":\"bin/my-app\"}");

                ExecutableDetector detector = new ExecutableDetector();

                string? result = await detector.FindExecutableAsync(serviceDirectory, "test-service");

                Assert.NotNull(result);
                Assert.Equal(Path.GetFullPath(executablePath), Path.GetFullPath(result));
            }
        }

        [Fact]
        public async Task FindExecutableAsync_ExactNameMatch_ReturnsExecutable()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceDirectory = tempHelper.CreateTempDirectory("service");
                string executablePath = tempHelper.CreateTempFile("service/my-service", "");

                ExecutableDetector detector = new ExecutableDetector();

                string? result = await detector.FindExecutableAsync(serviceDirectory, "my-service");

                Assert.NotNull(result);
                Assert.Equal(Path.GetFullPath(executablePath), Path.GetFullPath(result));
            }
        }

        [Fact]
        public async Task FindExecutableAsync_PartialNameMatch_ReturnsExecutable()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceDirectory = tempHelper.CreateTempDirectory("service");
                string executablePath = tempHelper.CreateTempFile("service/my-service-api", "");

                ExecutableDetector detector = new ExecutableDetector();

                string? result = await detector.FindExecutableAsync(serviceDirectory, "service");

                Assert.NotNull(result);
                Assert.Equal(Path.GetFullPath(executablePath), Path.GetFullPath(result));
            }
        }

        [Fact]
        public async Task FindExecutableAsync_NoMatch_ReturnsNull()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceDirectory = tempHelper.CreateTempDirectory("service");
                tempHelper.CreateTempFile("service/other-file.txt", "");
                tempHelper.CreateTempFile("service/readme.md", "");

                ExecutableDetector detector = new ExecutableDetector();

                string? result = await detector.FindExecutableAsync(serviceDirectory, "my-service");

                Assert.Null(result);
            }
        }

        [Fact]
        public async Task FindExecutableAsync_InSubdirectory_FindsExecutable()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceDirectory = tempHelper.CreateTempDirectory("service");
                string binDirectory = tempHelper.CreateTempDirectory("service/bin");
                string executablePath = tempHelper.CreateTempFile("service/bin/my-service", "");

                ExecutableDetector detector = new ExecutableDetector();

                string? result = await detector.FindExecutableAsync(serviceDirectory, "my-service");

                Assert.NotNull(result);
                Assert.Equal(Path.GetFullPath(executablePath), Path.GetFullPath(result));
            }
        }

        [Fact]
        public async Task FindExecutableAsync_InvalidUpdaemonJson_FallsBackToSearch()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceDirectory = tempHelper.CreateTempDirectory("service");
                string executablePath = tempHelper.CreateTempFile("service/my-service", "");
                tempHelper.CreateTempFile("service/updaemon.json", "invalid json {{{");

                ExecutableDetector detector = new ExecutableDetector();

                string? result = await detector.FindExecutableAsync(serviceDirectory, "my-service");

                Assert.NotNull(result);
                Assert.Equal(Path.GetFullPath(executablePath), Path.GetFullPath(result));
            }
        }

        [Fact]
        public async Task FindExecutableAsync_UpdateaemonJsonWithNonExistentPath_FallsBackToSearch()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceDirectory = tempHelper.CreateTempDirectory("service");
                string executablePath = tempHelper.CreateTempFile("service/my-service", "");
                tempHelper.CreateTempFile("service/updaemon.json", "{\"executablePath\":\"bin/nonexistent\"}");

                ExecutableDetector detector = new ExecutableDetector();

                string? result = await detector.FindExecutableAsync(serviceDirectory, "my-service");

                Assert.NotNull(result);
                Assert.Equal(Path.GetFullPath(executablePath), Path.GetFullPath(result));
            }
        }

        [Fact]
        public async Task FindExecutableAsync_ExactMatchPreferredOverPartialMatch()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceDirectory = tempHelper.CreateTempDirectory("service");
                string exactMatchPath = tempHelper.CreateTempFile("service/api", "");
                tempHelper.CreateTempFile("service/api-service", "");

                ExecutableDetector detector = new ExecutableDetector();

                string? result = await detector.FindExecutableAsync(serviceDirectory, "api");

                Assert.NotNull(result);
                Assert.Equal(Path.GetFullPath(exactMatchPath), Path.GetFullPath(result));
            }
        }

        [Fact]
        public async Task FindExecutableAsync_CaseInsensitiveMatch_FindsExecutable()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string serviceDirectory = tempHelper.CreateTempDirectory("service");
                string executablePath = tempHelper.CreateTempFile("service/MyService", "");

                ExecutableDetector detector = new ExecutableDetector();

                string? result = await detector.FindExecutableAsync(serviceDirectory, "myservice");

                Assert.NotNull(result);
                Assert.Equal(Path.GetFullPath(executablePath), Path.GetFullPath(result));
            }
        }
    }
}

