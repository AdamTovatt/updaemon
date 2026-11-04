using System.Net;
using System.Security.Cryptography;
using Updaemon.Common.Models;
using Updaemon.Common.Utilities;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    public class PluginDownloaderTests
    {
        private static readonly Uri Url = new Uri("https://api.github.com/repos/owner/repo/releases/assets/1");

        private static readonly Uri Sums = new Uri("https://api.github.com/repos/owner/repo/releases/assets/2");

        private static readonly byte[] Build = { 1, 2, 3 };

        /// <summary>Serves the build at <see cref="Url"/> and its SHA256SUMS at <see cref="Sums"/>, or answers every request with <see cref="Status"/>.</summary>
        private sealed class OnePlugin : HttpMessageHandler
        {
            public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

            /// <summary>Serves another build than the one its SHA256SUMS lists.</summary>
            public bool Tampered { get; init; }

            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                HttpContent content = request.RequestUri == Sums
                    ? new StringContent($"{Convert.ToHexStringLower(SHA256.HashData(Build))}  plugin-linux-arm64\n")
                    : new ByteArrayContent(Tampered ? new byte[] { 6, 6, 6 } : Build);
                return Task.FromResult(new HttpResponseMessage(Status) { Content = content });
            }
        }

        /// <summary>Finds no line for any asset, as in a release whose SHA256SUMS leaves the build out.</summary>
        private sealed class RefusingChecksums : IReleaseChecksums
        {
            public List<string> Checked { get; } = new List<string>();

            public Task<ExpectedSha256> ReadExpectedAsync(Uri checksumsUrl, string assetName, GitHubFetch? gitHub, CancellationToken cancellationToken = default)
            {
                Checked.Add(assetName);
                throw new ChecksumException("SHA256SUMS has no '<sha-256>  plugin-linux-arm64' line, so plugin-linux-arm64 cannot be verified.");
            }
        }

        private static PluginDownloader Downloader(OnePlugin server, IReleaseChecksums? checksums = null, MockDistributionServiceClient? client = null, MockFilePermissionManager? permissions = null)
        {
            HttpClient httpClient = new HttpClient(server);
            return new PluginDownloader(httpClient, checksums ?? new ReleaseChecksums(httpClient), client ?? new MockDistributionServiceClient(), permissions ?? new MockFilePermissionManager());
        }

        [Fact]
        public async Task DownloadAndInspectAsync_DownloadFailsItsChecksum_IsDeletedWithoutBeingRun()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                MockDistributionServiceClient client = new MockDistributionServiceClient();
                MockFilePermissionManager permissions = new MockFilePermissionManager();
                string directory = temp.GetFullPath("plugins");

                await Assert.ThrowsAsync<ChecksumException>(
                    () => Downloader(new OnePlugin { Tampered = true }, client: client, permissions: permissions).DownloadAndInspectAsync(new PluginDownload(Url, "plugin-linux-arm64", Sums, null), directory));

                Assert.Empty(client.MethodCalls);
                Assert.Empty(permissions.MethodCalls);
                Assert.Empty(Directory.GetFiles(directory));
            }
        }

        [Fact]
        public async Task DownloadAndInspectAsync_SumsHaveNoLineForTheBuild_FailsBeforeDownloadingIt()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                OnePlugin server = new OnePlugin();
                RefusingChecksums checksums = new RefusingChecksums();
                string directory = temp.GetFullPath("plugins");

                await Assert.ThrowsAsync<ChecksumException>(
                    () => Downloader(server, checksums).DownloadAndInspectAsync(new PluginDownload(Url, "plugin-linux-arm64", Sums, null), directory));

                Assert.Equal(new[] { "plugin-linux-arm64" }, checksums.Checked);
                Assert.Empty(server.Requests);
                Assert.False(Directory.Exists(directory));
            }
        }

        [Fact]
        public async Task DownloadAndInspectAsync_FromGitHubWithAToken_FetchesTheBuildAndItsSumsAsAssetsWithTheToken()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                OnePlugin github = new OnePlugin();
                MockDistributionServiceClient client = new MockDistributionServiceClient();

                await Downloader(github, client: client).DownloadAndInspectAsync(new PluginDownload(Url, "plugin-linux-arm64", Sums, new GitHubFetch("tok")), temp.GetFullPath("plugins"));

                Assert.Equal(new[] { Sums, Url }, github.Requests.Select(request => request.RequestUri));
                Assert.All(github.Requests, request =>
                {
                    Assert.Equal("Bearer tok", request.Headers.Authorization?.ToString());
                    Assert.Equal("application/octet-stream", request.Headers.Accept.ToString());
                });
                Assert.NotEmpty(client.MethodCalls);
            }
        }

        [Fact]
        public async Task DownloadAndInspectAsync_PlainUrl_IsFetchedAsItIsAndNotChecked()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                OnePlugin server = new OnePlugin();
                RefusingChecksums checksums = new RefusingChecksums();

                await Downloader(server, checksums).DownloadAndInspectAsync(new PluginDownload(Url, "plugin-linux-arm64", Checksums: null, GitHub: null), temp.GetFullPath("plugins"));

                Assert.Empty(checksums.Checked);
                HttpRequestMessage request = Assert.Single(server.Requests);
                Assert.Null(request.Headers.Authorization);
                Assert.Empty(request.Headers.Accept);
            }
        }

        [Fact]
        public async Task DownloadAndInspectAsync_GitHubRejectsTheToken_NeedsANewOne()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(
                    () => Downloader(new OnePlugin { Status = HttpStatusCode.Unauthorized })
                        .DownloadAndInspectAsync(new PluginDownload(Url, "plugin-linux-arm64", Sums, new GitHubFetch("old")), temp.GetFullPath("plugins")));

                Assert.Equal(CredentialProblem.Rejected, ex.Need.Problem);
            }
        }

        [Fact]
        public async Task DownloadAndInspectAsync_PlainUrlAnswers401_IsAnHttpErrorNotATokenNeed()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
                    () => Downloader(new OnePlugin { Status = HttpStatusCode.Unauthorized })
                        .DownloadAndInspectAsync(new PluginDownload(Url, "plugin-linux-arm64", Checksums: null, GitHub: null), temp.GetFullPath("plugins")));

                Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
            }
        }
    }
}
