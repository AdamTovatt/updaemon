using System.Net;
using Updaemon.Common.Utilities;
using Updaemon.Interfaces;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    public class PluginDownloaderTests
    {
        private const string Url = "https://github.com/owner/repo/releases/download/v1.0.0/plugin-linux-arm64";

        /// <summary>Serves the plugin binary at <see cref="Url"/>.</summary>
        private sealed class OnePlugin : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });
        }

        /// <summary>Refuses every download, as for a tampered one.</summary>
        private sealed class RefusingChecksums : IReleaseChecksums
        {
            public List<string> Checked { get; } = new List<string>();

            public Task VerifyAsync(string assetUrl, string filePath, CancellationToken cancellationToken = default)
            {
                Checked.Add(assetUrl);
                throw new ChecksumException("plugin-linux-arm64 does not match its SHA-256 in SHA256SUMS.");
            }
        }

        [Fact]
        public async Task DownloadAndInspectAsync_DownloadFailsItsChecksum_IsDeletedWithoutBeingRun()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                RefusingChecksums checksums = new RefusingChecksums();
                MockDistributionServiceClient client = new MockDistributionServiceClient();
                MockFilePermissionManager permissions = new MockFilePermissionManager();
                PluginDownloader downloader = new PluginDownloader(new HttpClient(new OnePlugin()), checksums, client, permissions, new MockOutputWriter());
                string directory = temp.GetFullPath("plugins");

                await Assert.ThrowsAsync<ChecksumException>(() => downloader.DownloadAndInspectAsync(Url, verify: true, directory));

                Assert.Equal(new[] { Url }, checksums.Checked);
                Assert.Empty(client.MethodCalls);
                Assert.Empty(permissions.MethodCalls);
                Assert.Empty(Directory.GetFiles(directory));
            }
        }

        [Fact]
        public async Task DownloadAndInspectAsync_NotToBeVerified_IsNotChecked()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                RefusingChecksums checksums = new RefusingChecksums();
                PluginDownloader downloader = new PluginDownloader(new HttpClient(new OnePlugin()), checksums, new MockDistributionServiceClient(), new MockFilePermissionManager(), new MockOutputWriter());

                await downloader.DownloadAndInspectAsync(Url, verify: false, temp.GetFullPath("plugins"));

                Assert.Empty(checksums.Checked);
            }
        }
    }
}
