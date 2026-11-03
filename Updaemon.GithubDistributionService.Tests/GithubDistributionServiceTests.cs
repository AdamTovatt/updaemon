using Updaemon.Common;
using Updaemon.Common.Utilities;
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
                    new GithubAsset { Id = 101, Name = "app-linux-arm.zip", BrowserDownloadUrl = "https://example.com/app-linux-arm.zip" },
                    new GithubAsset { Id = 102, Name = "app-linux-x64.zip", BrowserDownloadUrl = "https://example.com/app-linux-x64.zip" },
                    new GithubAsset { Id = 103, Name = "app-windows.zip", BrowserDownloadUrl = "https://example.com/app-windows.zip" },
                },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);
            string targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            await service.DownloadVersionAsync("owner/repo/*-linux-arm.zip", new Version(1, 2, 3), "linux-x64", targetPath);

            await apiClient.Received(1).DownloadReleaseAssetAsync(
                Arg.Is<string>(value => value == "owner"),
                Arg.Is<string>(value => value == "repo"),
                Arg.Is<long>(value => value == 101),
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
                    new GithubAsset { Id = 201, Name = "app.zip", BrowserDownloadUrl = "https://example.com/app.zip" },
                    new GithubAsset { Id = 202, Name = "app.tar.gz", BrowserDownloadUrl = "https://example.com/app.tar.gz" },
                },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);
            string targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            await service.DownloadVersionAsync("owner/repo/*.zip", new Version(1, 2, 3), "linux-arm64", targetPath);

            await apiClient.Received(1).DownloadReleaseAssetAsync(
                Arg.Is<string>(value => value == "owner"),
                Arg.Is<string>(value => value == "repo"),
                Arg.Is<long>(value => value == 201),
                Arg.Any<string>(),
                Arg.Is<string?>(token => token == null),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData("linux-arm64", 302)]
        [InlineData("linux-arm", 303)]
        [InlineData("osx-arm64", 304)]
        [InlineData("LINUX-X64", 301)]
        public async Task DownloadVersionAsync_WithoutPattern_PicksTheBuildForTheRuntime(string runtimeIdentifier, long expectedAssetId)
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[]
                {
                    new GithubAsset { Id = 301, Name = "app-linux-x64.zip" },
                    new GithubAsset { Id = 302, Name = "app-linux-arm64.zip" },
                    new GithubAsset { Id = 303, Name = "app-linux-arm.zip" },
                    new GithubAsset { Id = 304, Name = "app-osx-arm64" },
                    new GithubAsset { Id = 305, Name = "app-osx-arm64.sha256" },
                },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);
            string targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            await service.DownloadVersionAsync("owner/repo", new Version(1, 2, 3), runtimeIdentifier, targetPath);

            await apiClient.Received(1).DownloadReleaseAssetAsync(
                Arg.Is<string>(value => value == "owner"),
                Arg.Is<string>(value => value == "repo"),
                Arg.Is<long>(value => value == expectedAssetId),
                Arg.Any<string>(),
                Arg.Is<string?>(token => token == null),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DownloadVersionAsync_WithoutPatternAndNoBuildForTheRuntime_ListsAssetsOnePerLine()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v0.1.1",
                Assets = new[]
                {
                    new GithubAsset { Name = "MinerUHost-linux-arm64-framework-dependent.zip" },
                    new GithubAsset { Name = "MinerUHost-linux-arm64-self-contained.zip" },
                },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DownloadVersionAsync("owner/repo", new Version(0, 1, 1), "linux-arm64", "/tmp/test"));

            Assert.Equal(
                "No asset in the release v0.1.1 of owner/repo is named *-linux-arm64.zip or *-linux-arm64, the build for this machine. Its assets:\n" +
                "  MinerUHost-linux-arm64-framework-dependent.zip\n" +
                "  MinerUHost-linux-arm64-self-contained.zip\n" +
                "Pick one with the remote owner/repo/<pattern>, where <pattern> is an asset name and * matches any text.",
                exception.Message);
        }

        [Fact]
        public async Task DownloadVersionAsync_ReleaseWithoutAssets_SaysSo()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(new GithubRelease { TagName = "v1.2.3" });
            GithubDistributionService service = new GithubDistributionService(apiClient, Substitute.For<IVersionParser>(), Substitute.For<IDownloadPostProcessor>());

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DownloadVersionAsync("owner/repo", new Version(1, 2, 3), "linux-arm64", "/tmp/test"));

            Assert.Equal("The release v1.2.3 of owner/repo has no assets.", exception.Message);
        }

        [Fact]
        public async Task GetLatestVersionAsync_RemoteWithoutRepo_SaysWhatFormIsExpected()
        {
            GithubDistributionService service = new GithubDistributionService(
                Substitute.For<IGithubApiClient>(), Substitute.For<IVersionParser>(), Substitute.For<IDownloadPostProcessor>());

            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
                () => service.GetLatestVersionAsync("mineru-host"));

            Assert.Equal("Invalid GitHub remote 'mineru-host': expected owner/repo or owner/repo/<pattern>.", exception.Message);
        }

        [Fact]
        public async Task DownloadVersionAsync_WithoutPatternAndSeveralBuildsForTheRuntime_ThrowsException()
        {
            IGithubApiClient apiClient = Substitute.For<IGithubApiClient>();
            IVersionParser versionParser = Substitute.For<IVersionParser>();
            IDownloadPostProcessor postProcessor = Substitute.For<IDownloadPostProcessor>();
            GithubRelease release = new GithubRelease
            {
                TagName = "v1.2.3",
                Assets = new[]
                {
                    new GithubAsset { Name = "app-linux-arm64.zip" },
                    new GithubAsset { Name = "app-linux-arm64" },
                },
            };
            apiClient.GetLatestReleaseAsync("owner", "repo", null, Arg.Any<CancellationToken>())
                .Returns(release);
            GithubDistributionService service = new GithubDistributionService(apiClient, versionParser, postProcessor);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DownloadVersionAsync("owner/repo", new Version(1, 2, 3), "linux-arm64", "/tmp/test"));

            Assert.StartsWith("Several assets in the release v1.2.3 of owner/repo are named *-linux-arm64.zip or *-linux-arm64", exception.Message);
            Assert.Contains("\n  app-linux-arm64.zip\n  app-linux-arm64\n", exception.Message);
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
                () => service.DownloadVersionAsync("owner/repo/*-linux-*.zip", new Version(1, 2, 3), "linux-arm", "/tmp/test"));

            Assert.StartsWith("Several assets in the release v1.2.3 of owner/repo match '*-linux-*.zip'.", exception.Message);
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
                () => service.DownloadVersionAsync("owner/repo/*.zip", new Version(1, 2, 3), "linux-arm64", "/tmp/test"));

            Assert.StartsWith("No asset in the release v1.2.3 of owner/repo matches '*.zip'.", exception.Message);
            Assert.Contains("\n  app.tar.gz\n", exception.Message);
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

            await service.DownloadVersionAsync("owner/repo/app.zip", new Version(1, 2, 3), "linux-arm64", targetPath);

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
                await service.DownloadVersionAsync("owner/repo/app.zip", new Version(1, 2, 3), "linux-arm64", targetPath);

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
