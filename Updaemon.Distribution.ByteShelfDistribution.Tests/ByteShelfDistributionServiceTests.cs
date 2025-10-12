using ByteShelfCommon;
using Updaemon.Common;
using Updaemon.Common.Utilities;
using Updaemon.Distribution.ByteShelfDistribution.Interfaces;

namespace Updaemon.Distribution.ByteShelfDistribution.Tests
{
    public class ByteShelfDistributionServiceTests
    {
        [Fact]
        public async Task InitializeAsync_WithMissingApiKey_ThrowsException()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);
            SecretCollection secrets = new SecretCollection(new Dictionary<string, string>
            {
                { "byteShelfUrl", "https://example.com" },
            });

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.InitializeAsync(secrets));

            Assert.Contains("ByteShelf API key is required", exception.Message);
        }

        [Fact]
        public async Task InitializeAsync_WithMissingUrl_ThrowsException()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);
            SecretCollection secrets = new SecretCollection(new Dictionary<string, string>
            {
                { "byteShelfApiKey", "test-key" },
            });

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.InitializeAsync(secrets));

            Assert.Contains("ByteShelf URL is required", exception.Message);
        }

        [Fact]
        public async Task InitializeAsync_WithBothSecrets_DoesNotThrow()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);
            SecretCollection secrets = new SecretCollection(new Dictionary<string, string>
            {
                { "byteShelfApiKey", "test-key" },
                { "byteShelfUrl", "https://example.com" },
            });

            await service.InitializeAsync(secrets);

            // Should complete without throwing
        }

        [Fact]
        public async Task InitializeAsync_CaseInsensitiveSecrets_Works()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);
            SecretCollection secrets = SecretCollection.FromString("BYTESHELFAPIKEY=test-key\nBYTESHELFURL=https://example.com");

            await service.InitializeAsync(secrets);

            // Should complete without throwing
        }

        [Fact]
        public async Task GetLatestVersionAsync_WithoutInitialization_ThrowsException()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetLatestVersionAsync("MyApp"));

            Assert.Contains("API client not initialized", exception.Message);
        }

        [Fact]
        public async Task DownloadVersionAsync_WithoutInitialization_ThrowsException()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DownloadVersionAsync("MyApp", new Version(1, 0, 0), "/tmp/test"));

            Assert.Contains("API client not initialized", exception.Message);
        }

        [Fact]
        public void ParseServiceName_WithSubtenantOnly_ReturnsCorrectValues()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);

            // Use reflection to call private method for testing
            System.Reflection.MethodInfo? parseMethod = typeof(ByteShelfDistributionService)
                .GetMethod("ParseServiceName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            object? result = parseMethod?.Invoke(service, new object[] { "MyApp" });
            (string appSubtenantName, string? filenamePattern) = ((string, string?))result!;

            Assert.Equal("MyApp", appSubtenantName);
            Assert.Null(filenamePattern);
        }

        [Fact]
        public void ParseServiceName_WithSubtenantAndPattern_ReturnsCorrectValues()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);

            System.Reflection.MethodInfo? parseMethod = typeof(ByteShelfDistributionService)
                .GetMethod("ParseServiceName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            object? result = parseMethod?.Invoke(service, new object[] { "MyApp/linux-*.zip" });
            (string appSubtenantName, string? filenamePattern) = ((string, string?))result!;

            Assert.Equal("MyApp", appSubtenantName);
            Assert.Equal("linux-*.zip", filenamePattern);
        }

        [Fact]
        public void ParseServiceName_WithLeadingSlash_HandlesGracefully()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);

            System.Reflection.MethodInfo? parseMethod = typeof(ByteShelfDistributionService)
                .GetMethod("ParseServiceName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            object? result = parseMethod?.Invoke(service, new object[] { "/MyApp" });
            (string appSubtenantName, string? filenamePattern) = ((string, string?))result!;

            Assert.Equal("MyApp", appSubtenantName);
        }

        [Fact]
        public void ParseServiceName_WithTrailingSlash_HandlesGracefully()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);

            System.Reflection.MethodInfo? parseMethod = typeof(ByteShelfDistributionService)
                .GetMethod("ParseServiceName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            object? result = parseMethod?.Invoke(service, new object[] { "MyApp/" });
            (string appSubtenantName, string? filenamePattern) = ((string, string?))result!;

            Assert.Equal("MyApp", appSubtenantName);
        }

        [Fact]
        public void FindMatchingFile_WithNoPatternAndSingleFile_ReturnsFile()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);

            Guid fileId = Guid.NewGuid();
            ShelfFileMetadata[] files = new[]
            {
                new ShelfFileMetadata(fileId, "app.zip", "application/zip", 1024, new List<Guid>()),
            };

            System.Reflection.MethodInfo? findMethod = typeof(ByteShelfDistributionService)
                .GetMethod("FindMatchingFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            ShelfFileMetadata? result = findMethod?.Invoke(service, new object?[] { files, null, "MyApp", "1.0.0" }) as ShelfFileMetadata;

            Assert.NotNull(result);
            Assert.Equal("app.zip", result.OriginalFilename);
        }

        [Fact]
        public void FindMatchingFile_WithNoPatternAndMultipleFiles_ThrowsException()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);

            ShelfFileMetadata[] files = new[]
            {
                new ShelfFileMetadata(Guid.NewGuid(), "file1.zip", "application/zip", 1024, new List<Guid>()),
                new ShelfFileMetadata(Guid.NewGuid(), "file2.zip", "application/zip", 1024, new List<Guid>()),
            };

            System.Reflection.MethodInfo? findMethod = typeof(ByteShelfDistributionService)
                .GetMethod("FindMatchingFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            System.Reflection.TargetInvocationException exception = Assert.Throws<System.Reflection.TargetInvocationException>(
                () => findMethod?.Invoke(service, new object?[] { files, null, "MyApp", "1.0.0" }));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("Multiple files found", exception.InnerException.Message);
        }

        [Fact]
        public void FindMatchingFile_WithWildcardPattern_MatchesCorrectFile()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);

            ShelfFileMetadata[] files = new[]
            {
                new ShelfFileMetadata(Guid.NewGuid(), "app-linux-arm.zip", "application/zip", 1024, new List<Guid>()),
                new ShelfFileMetadata(Guid.NewGuid(), "app-linux-x64.zip", "application/zip", 1024, new List<Guid>()),
                new ShelfFileMetadata(Guid.NewGuid(), "app-windows.zip", "application/zip", 1024, new List<Guid>()),
            };

            System.Reflection.MethodInfo? findMethod = typeof(ByteShelfDistributionService)
                .GetMethod("FindMatchingFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            ShelfFileMetadata? result = findMethod?.Invoke(service, new object?[] { files, "*-linux-arm.zip", "MyApp", "1.0.0" }) as ShelfFileMetadata;

            Assert.NotNull(result);
            Assert.Equal("app-linux-arm.zip", result.OriginalFilename);
        }

        [Fact]
        public void FindMatchingFile_WithPatternMatchingMultiple_ThrowsException()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);

            ShelfFileMetadata[] files = new[]
            {
                new ShelfFileMetadata(Guid.NewGuid(), "app-linux-arm.zip", "application/zip", 1024, new List<Guid>()),
                new ShelfFileMetadata(Guid.NewGuid(), "app-linux-x64.zip", "application/zip", 1024, new List<Guid>()),
            };

            System.Reflection.MethodInfo? findMethod = typeof(ByteShelfDistributionService)
                .GetMethod("FindMatchingFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            System.Reflection.TargetInvocationException exception = Assert.Throws<System.Reflection.TargetInvocationException>(
                () => findMethod?.Invoke(service, new object?[] { files, "*-linux-*.zip", "MyApp", "1.0.0" }));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("Multiple files match pattern", exception.InnerException.Message);
        }

        [Fact]
        public void FindMatchingFile_WithPatternMatchingNone_ThrowsException()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);

            ShelfFileMetadata[] files = new[]
            {
                new ShelfFileMetadata(Guid.NewGuid(), "app.tar.gz", "application/gzip", 1024, new List<Guid>()),
            };

            System.Reflection.MethodInfo? findMethod = typeof(ByteShelfDistributionService)
                .GetMethod("FindMatchingFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            System.Reflection.TargetInvocationException exception = Assert.Throws<System.Reflection.TargetInvocationException>(
                () => findMethod?.Invoke(service, new object?[] { files, "*.zip", "MyApp", "1.0.0" }));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("No files matching pattern", exception.InnerException.Message);
        }

        [Fact]
        public void FindMatchingFile_CaseInsensitiveMatching_Works()
        {
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            ByteShelfDistributionService service = new ByteShelfDistributionService(versionParser, postProcessor);

            ShelfFileMetadata[] files = new[]
            {
                new ShelfFileMetadata(Guid.NewGuid(), "App-LINUX-X64.ZIP", "application/zip", 1024, new List<Guid>()),
            };

            System.Reflection.MethodInfo? findMethod = typeof(ByteShelfDistributionService)
                .GetMethod("FindMatchingFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            ShelfFileMetadata? result = findMethod?.Invoke(service, new object?[] { files, "app-linux-*.zip", "MyApp", "1.0.0" }) as ShelfFileMetadata;

            Assert.NotNull(result);
            Assert.Equal("App-LINUX-X64.ZIP", result.OriginalFilename);
        }
    }
}

