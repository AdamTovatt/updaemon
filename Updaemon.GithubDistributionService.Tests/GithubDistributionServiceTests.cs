using Updaemon.Common;
using Updaemon.GithubDistributionService.Interfaces;
using Updaemon.GithubDistributionService.Models;

namespace Updaemon.GithubDistributionService.Tests
{
    public class GithubDistributionServiceTests
    {
        [Fact]
        public async Task InitializeAsync_WithNoSecrets_DoesNotThrow()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            await service.InitializeAsync(new SecretCollection(new Dictionary<string, string>()));

            // Should complete without throwing
        }

        [Fact]
        public async Task InitializeAsync_WithGithubToken_ParsesToken()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);
            SecretCollection secrets = SecretCollection.FromString("githubToken=test-token-123\napiKey=other-value");

            await service.InitializeAsync(secrets);

            // Token should be used in subsequent calls - we'll verify this indirectly
        }

        [Fact]
        public async Task GetLatestVersionAsync_WithValidRelease_ReturnsVersion()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[] { new GithubAsset { Name = "app.zip", BrowserDownloadUrl = "https://example.com/app.zip" } },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            versionParser.Parse("v1.2.3").Returns(new Version(1, 2, 3));
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            Version? result = await service.GetLatestVersionAsync("owner/repo");

            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3), result);
        }

        [Fact]
        public async Task GetLatestVersionAsync_IgnoresFilenamePattern()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[]
                {
                    new GithubAsset { Name = "linux-arm.zip", BrowserDownloadUrl = "https://example.com/linux-arm.zip" },
                    new GithubAsset { Name = "linux-x64.zip", BrowserDownloadUrl = "https://example.com/linux-x64.zip" },
                },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            versionParser.Parse("v1.2.3").Returns(new Version(1, 2, 3));
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            // Even with pattern, GetLatestVersion just returns version from tag
            Version? result = await service.GetLatestVersionAsync("owner/repo/*.zip");

            Assert.NotNull(result);
            Assert.Equal(new Version(1, 2, 3), result);
        }

        [Fact]
        public async Task GetLatestVersionAsync_WithLeadingSlash_HandlesGracefully()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[] { new GithubAsset { Name = "app.zip", BrowserDownloadUrl = "https://example.com/app.zip" } },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            versionParser.Parse("v1.2.3").Returns(new Version(1, 2, 3));
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            Version? result = await service.GetLatestVersionAsync("/owner/repo");

            Assert.NotNull(result);
        }

        [Fact]
        public async Task GetLatestVersionAsync_WithTrailingSlash_HandlesGracefully()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[] { new GithubAsset { Name = "app.zip", BrowserDownloadUrl = "https://example.com/app.zip" } },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            versionParser.Parse("v1.2.3").Returns(new Version(1, 2, 3));
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            Version? result = await service.GetLatestVersionAsync("owner/repo/");

            Assert.NotNull(result);
        }

        [Fact]
        public async Task GetLatestVersionAsync_NoReleaseFound_ReturnsNull()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns((GithubRelease?)null);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            Version? result = await service.GetLatestVersionAsync("owner/repo");

            Assert.Null(result);
        }

        [Fact]
        public async Task DownloadVersionAsync_WithWildcardPattern_DownloadsMatchingAsset()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[]
                {
                    new GithubAsset { Name = "app-linux-arm.zip", BrowserDownloadUrl = "https://example.com/app-linux-arm.zip" },
                    new GithubAsset { Name = "app-linux-x64.zip", BrowserDownloadUrl = "https://example.com/app-linux-x64.zip" },
                    new GithubAsset { Name = "app-windows.zip", BrowserDownloadUrl = "https://example.com/app-windows.zip" },
                },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);
            string targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            await service.DownloadVersionAsync("owner/repo/*-linux-arm.zip", new Version(1, 2, 3), targetPath);

            await apiClient.Received(1).DownloadAssetAsync(
                Arg.Is<string>(url => url == "https://example.com/app-linux-arm.zip"),
                Arg.Any<string>(),
                Arg.Is<string?>(token => token == null),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DownloadVersionAsync_WithSimpleWildcard_DownloadsMatchingAsset()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[]
                {
                    new GithubAsset { Name = "app.zip", BrowserDownloadUrl = "https://example.com/app.zip" },
                    new GithubAsset { Name = "app.tar.gz", BrowserDownloadUrl = "https://example.com/app.tar.gz" },
                },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);
            string targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            await service.DownloadVersionAsync("owner/repo/*.zip", new Version(1, 2, 3), targetPath);

            await apiClient.Received(1).DownloadAssetAsync(
                Arg.Is<string>(url => url == "https://example.com/app.zip"),
                Arg.Any<string>(),
                Arg.Is<string?>(token => token == null),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DownloadVersionAsync_WithoutPattern_UsesSingleAsset()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[] { new GithubAsset { Name = "app.zip", BrowserDownloadUrl = "https://example.com/app.zip" } },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);
            string targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            await service.DownloadVersionAsync("owner/repo", new Version(1, 2, 3), targetPath);

            await apiClient.Received(1).DownloadAssetAsync(
                Arg.Is<string>(url => url == "https://example.com/app.zip"),
                Arg.Any<string>(),
                Arg.Is<string?>(token => token == null),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DownloadVersionAsync_WithoutPatternAndMultipleAssets_ThrowsException()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[]
                {
                    new GithubAsset { Name = "file1.zip", BrowserDownloadUrl = "https://example.com/file1.zip" },
                    new GithubAsset { Name = "file2.zip", BrowserDownloadUrl = "https://example.com/file2.zip" },
                },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DownloadVersionAsync("owner/repo", new Version(1, 2, 3), "/tmp/test"));

            Assert.Contains("Multiple assets found", exception.Message);
        }

        [Fact]
        public async Task DownloadVersionAsync_WithPatternMatchingMultiple_ThrowsException()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[]
                {
                    new GithubAsset { Name = "app-linux-arm.zip", BrowserDownloadUrl = "https://example.com/app-linux-arm.zip" },
                    new GithubAsset { Name = "app-linux-x64.zip", BrowserDownloadUrl = "https://example.com/app-linux-x64.zip" },
                },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DownloadVersionAsync("owner/repo/*-linux-*.zip", new Version(1, 2, 3), "/tmp/test"));

            Assert.Contains("Multiple assets match pattern", exception.Message);
        }

        [Fact]
        public async Task DownloadVersionAsync_WithPatternMatchingNone_ThrowsException()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[] { new GithubAsset { Name = "app.tar.gz", BrowserDownloadUrl = "https://example.com/app.tar.gz" } },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DownloadVersionAsync("owner/repo/*.zip", new Version(1, 2, 3), "/tmp/test"));

            Assert.Contains("No assets matching pattern", exception.Message);
        }

        [Fact]
        public async Task DownloadVersionAsync_CallsPostProcessor()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[] { new GithubAsset { Name = "app.zip", BrowserDownloadUrl = "https://example.com/app.zip" } },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);
            string targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            await service.DownloadVersionAsync("owner/repo", new Version(1, 2, 3), targetPath);

            await postProcessor.Received(1).ProcessAsync(targetPath, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DownloadVersionAsync_CreatesTargetDirectory()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[] { new GithubAsset { Name = "app.zip", BrowserDownloadUrl = "https://example.com/app.zip" } },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);
            string targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                await service.DownloadVersionAsync("owner/repo", new Version(1, 2, 3), targetPath);

                Assert.True(Directory.Exists(targetPath));
            }
            finally
            {
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, true);
                }
            }
        }

        [Fact]
        public async Task InitializeAsync_WithGithubToken_UsesTokenInApiCalls()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[] { new GithubAsset { Name = "app.zip", BrowserDownloadUrl = "https://example.com/app.zip" } },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", "test-token", Arg.Any<CancellationToken>())
                .Returns(release);
            versionParser.Parse("v1.2.3").Returns(new Version(1, 2, 3));
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            await service.InitializeAsync(SecretCollection.FromString("githubToken=test-token"));
            Version? result = await service.GetLatestVersionAsync("owner/repo");

            Assert.NotNull(result);
            await apiClient.Received(1).GetLatestReleaseAsync("owner", "repo", "test-token", Arg.Any<CancellationToken>());
        }
    }
}
