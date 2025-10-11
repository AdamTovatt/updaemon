using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Helpers
{
    public class TestVersionExtractorTests
    {
        private readonly TestVersionExtractor _versionExtractor;

        public TestVersionExtractorTests()
        {
            _versionExtractor = new TestVersionExtractor();
        }

        [Fact]
        public void ExtractVersionFromPath_WithWindowsStylePath_ReturnsVersion()
        {
            // Arrange
            string path = @"C:\opt\app-name\1.2.3\app-name.exe";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3), result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithLinuxStylePath_ReturnsVersion()
        {
            // Arrange
            string path = "/opt/app-name/1.2.3/app-name";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3), result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithMixedSeparators_ReturnsVersion()
        {
            // Arrange
            string path = @"C:\opt/app-name\2.5.1/app-name.exe";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(new Version(2, 5, 1), result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithWindowsPathNoVersion_ReturnsNull()
        {
            // Arrange
            string path = @"C:\opt\app-name\current\app-name.exe";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithNullPath_ReturnsNull()
        {
            // Arrange
            string? path = null;

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithEmptyString_ReturnsNull()
        {
            // Arrange
            string path = "";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithMultipleVersionsInWindowsPath_ReturnsFirstVersion()
        {
            // Arrange
            string path = @"C:\apps\1.0.0\app-name\2.3.4\executable.exe";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(new Version(1, 0, 0), result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithUNCPath_ReturnsVersion()
        {
            // Arrange
            string path = @"\\server\share\app-name\3.2.1\app.exe";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(new Version(3, 2, 1), result);
        }
    }
}

