using Updaemon.Common.Models;
using Updaemon.Common.Utilities;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Downloads distribution plugin binaries, makes them executable, and retrieves their metadata.
    /// </summary>
    public class PluginDownloader : IPluginDownloader
    {
        private readonly HttpClient _httpClient;
        private readonly IReleaseChecksums _checksums;
        private readonly IDistributionServiceClient _distributionClient;
        private readonly IFilePermissionManager _filePermissionManager;

        public PluginDownloader(
            HttpClient httpClient,
            IReleaseChecksums checksums,
            IDistributionServiceClient distributionClient,
            IFilePermissionManager filePermissionManager)
        {
            _httpClient = httpClient;
            _checksums = checksums;
            _distributionClient = distributionClient;
            _filePermissionManager = filePermissionManager;
        }

        public async Task<PluginDownloadResult> DownloadAndInspectAsync(PluginDownload download, string targetDirectory, CancellationToken cancellationToken = default)
        {
            ExpectedSha256? expected = download.Checksums == null
                ? null
                : await _checksums.ReadExpectedAsync(download.Checksums, download.FileName, download.GitHub, cancellationToken);

            byte[] pluginData;
            using (HttpResponseMessage response = await ReleaseAsset.GetAsync(_httpClient, download.Build, download.GitHub, cancellationToken))
            {
                pluginData = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }

            Directory.CreateDirectory(targetDirectory);

            string tempPath = Path.Combine(targetDirectory, $"temp_{Guid.NewGuid():N}");
            try
            {
                await File.WriteAllBytesAsync(tempPath, pluginData, cancellationToken);

                // Before it is made executable and run to read its service information.
                if (expected != null)
                {
                    await expected.VerifyAsync(tempPath, cancellationToken);
                }

                await _filePermissionManager.SetExecutablePermissionsAsync(tempPath, cancellationToken);

                DistributionServiceInformation serviceInfo = await ConnectAndGetInfoAsync(tempPath, cancellationToken);

                return new PluginDownloadResult
                {
                    TempFilePath = tempPath,
                    ServiceInformation = serviceInfo,
                };
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }
                throw;
            }
        }

        private async Task<DistributionServiceInformation> ConnectAndGetInfoAsync(string pluginPath, CancellationToken cancellationToken)
        {
            try
            {
                return await _distributionClient.ConnectAsync(pluginPath, cancellationToken);
            }
            finally
            {
                await _distributionClient.DisposeAsync();
            }
        }
    }
}
