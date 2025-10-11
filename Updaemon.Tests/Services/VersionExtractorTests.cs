using Updaemon.Services;

namespace Updaemon.Tests.Services
{
    public class VersionExtractorTests
    {
        private readonly VersionExtractor _versionExtractor;

        public VersionExtractorTests()
        {
            _versionExtractor = new VersionExtractor();
        }

        [Fact]
        public void ExtractVersionFromPath_WithValidLinuxPath_ReturnsVersion()
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
        public void ExtractVersionFromPath_WithTwoPartVersion_ReturnsVersion()
        {
            // Arrange
            string path = "/opt/myservice/2.5/myservice";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(new Version(2, 5), result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithFourPartVersion_ReturnsVersion()
        {
            // Arrange
            string path = "/opt/app/1.2.3.4/executable";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3, 4), result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithMultipleVersionSegments_ReturnsFirstVersion()
        {
            // Arrange
            string path = "/opt/1.0.0/app-name/2.3.4/executable";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(new Version(1, 0, 0), result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithNoVersion_ReturnsNull()
        {
            // Arrange
            string path = "/opt/app-name/current/app-name";

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
        public void ExtractVersionFromPath_WithMalformedVersion_ReturnsNull()
        {
            // Arrange
            string path = "/opt/app-name/1.2.x/app-name";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithVersionInFilename_ReturnsVersion()
        {
            // Arrange
            string path = "/opt/app-name/3.4.5";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(new Version(3, 4, 5), result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithTrailingSlash_ReturnsVersion()
        {
            // Arrange
            string path = "/opt/app-name/1.2.3/";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3), result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithLeadingSlash_ReturnsVersion()
        {
            // Arrange
            string path = "/1.2.3/app-name";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3), result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithOnlyVersion_ReturnsVersion()
        {
            // Arrange
            string path = "1.2.3";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3), result);
        }

        [Fact]
        public void ExtractVersionFromPath_WithNumbersNotVersion_ReturnsNull()
        {
            // Arrange
            string path = "/opt/123/app-name/current";

            // Act
            Version? result = _versionExtractor.ExtractVersionFromPath(path);

            // Assert
            Assert.Null(result);
        }
    }
}

