using Updaemon.Common.Models;
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
        private readonly IOutputWriter _outputWriter;

        public PluginDownloader(
            HttpClient httpClient,
            IReleaseChecksums checksums,
            IDistributionServiceClient distributionClient,
            IFilePermissionManager filePermissionManager,
            IOutputWriter outputWriter)
        {
            _httpClient = httpClient;
            _checksums = checksums;
            _distributionClient = distributionClient;
            _filePermissionManager = filePermissionManager;
            _outputWriter = outputWriter;
        }

        public async Task<PluginDownloadResult> DownloadAndInspectAsync(string downloadUrl, bool verify, string targetDirectory, CancellationToken cancellationToken = default)
        {
            byte[] pluginData = await _httpClient.GetByteArrayAsync(downloadUrl, cancellationToken);
            _outputWriter.WriteLine($"Downloaded {pluginData.Length} bytes");

            Directory.CreateDirectory(targetDirectory);

            string tempPath = Path.Combine(targetDirectory, $"temp_{Guid.NewGuid():N}");
            try
            {
                await File.WriteAllBytesAsync(tempPath, pluginData, cancellationToken);

                // Before it is made executable and run to read its service information.
                if (verify)
                {
                    await _checksums.VerifyAsync(downloadUrl, tempPath, cancellationToken);
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
