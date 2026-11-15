using System.Security.Cryptography;
using System.Text;
using Updaemon.Common;
using Updaemon.Common.Models;
using Updaemon.Common.Utilities;
using Updaemon.GithubDistributionService.Interfaces;
using Updaemon.GithubDistributionService.Models;

namespace Updaemon.GithubDistributionService.Tests
{
    /// <summary>The release's SHA256SUMS read through GitHub's API; the rules themselves are ReleaseAssetsTests'.</summary>
    public class GithubDistributionServiceChecksumTests : IDisposable
    {
        private readonly string _targetPath = Path.Combine(Path.GetTempPath(), $"github-checksums-{Guid.NewGuid():N}");

        private readonly IDownloadPostProcessor _postProcessor = Substitute.For<IDownloadPostProcessor>();

        /// <summary>A release whose assets have these contents, downloadable by id.</summary>
        private sealed class FakeGithub : IGithubApiClient
        {
            private readonly Dictionary<long, string> _contents = new Dictionary<long, string>();
            private readonly List<GithubAsset> _assets = new List<GithubAsset>();

            public List<long> Downloaded { get; } = new List<long>();

            public FakeGithub With(string name, string content)
            {
                long id = _assets.Count + 1;
                _assets.Add(new GithubAsset { Id = id, Name = name });
                _contents[id] = content;
                return this;
            }

            public FakeGithub WithSums(params (string Name, string Content)[] listed) =>
                With(Sha256Sums.AssetName, SumsOf(listed));

            public Task<GithubRelease?> GetLatestReleaseAsync(string owner, string repo, string? token, CancellationToken cancellationToken = default) =>
                Task.FromResult<GithubRelease?>(new GithubRelease { TagName = "v1.2.3", Assets = _assets.ToArray() });

            public Task DownloadReleaseAssetAsync(string owner, string repo, long assetId, string targetFilePath, string? token, CancellationToken cancellationToken = default)
            {
                Downloaded.Add(assetId);
                return File.WriteAllTextAsync(targetFilePath, _contents[assetId], cancellationToken);
            }

            public Task<string> ReadReleaseAssetTextAsync(string owner, string repo, long assetId, string? token, CancellationToken cancellationToken = default) =>
                Task.FromResult(_contents[assetId]);

            public Task DownloadAssetAsync(string url, string targetFilePath, string? token, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();
        }

        private static string Sha256(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        /// <summary>A SHA256SUMS listing each name with the hash of its content.</summary>
        private static string SumsOf(params (string Name, string Content)[] listed) =>
            string.Concat(listed.Select(entry => $"{Sha256(entry.Content)}  {entry.Name}\n"));

        private Task<DownloadResult> DownloadAsync(FakeGithub github, string remote = "owner/repo") =>
            new GithubDistributionService(github, Substitute.For<IVersionParser>(), _postProcessor).DownloadVersionAsync(remote, new Version(1, 2, 3), "linux-arm64", _targetPath);

        public void Dispose()
        {
            if (Directory.Exists(_targetPath))
            {
                Directory.Delete(_targetPath, recursive: true);
            }
        }

        [Fact]
        public async Task DownloadVersionAsync_AssetMatchesItsLine_IsVerifiedThenExtracted()
        {
            FakeGithub github = new FakeGithub().With("app-linux-arm64.zip", "build").WithSums(("app-linux-arm64.zip", "build"));

            DownloadResult result = await DownloadAsync(github);

            Assert.True(result.Verified);
            Assert.Equal(new[] { "app-linux-arm64.zip" }, Directory.GetFiles(_targetPath).Select(Path.GetFileName));
            await _postProcessor.Received(1).ProcessAsync(_targetPath, Arg.Any<CancellationToken>());
        }

    }
}
