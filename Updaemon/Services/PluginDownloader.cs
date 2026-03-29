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
        private readonly IDistributionServiceClient _distributionClient;
        private readonly IFilePermissionManager _filePermissionManager;
        private readonly IOutputWriter _outputWriter;

        public PluginDownloader(
            HttpClient httpClient,
            IDistributionServiceClient distributionClient,
            IFilePermissionManager filePermissionManager,
            IOutputWriter outputWriter)
        {
            _httpClient = httpClient;
            _distributionClient = distributionClient;
            _filePermissionManager = filePermissionManager;
            _outputWriter = outputWriter;
        }

        public async Task<PluginDownloadResult> DownloadAndInspectAsync(string downloadUrl, string targetDirectory, CancellationToken cancellationToken = default)
        {
            byte[] pluginData = await _httpClient.GetByteArrayAsync(downloadUrl, cancellationToken);
            _outputWriter.WriteLine($"Downloaded {pluginData.Length} bytes");

            Directory.CreateDirectory(targetDirectory);

            string tempPath = Path.Combine(targetDirectory, $"temp_{Guid.NewGuid():N}");
            try
            {
                await File.WriteAllBytesAsync(tempPath, pluginData, cancellationToken);

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

        public async Task<DistributionServiceInformation> InspectLocalAsync(string pluginPath, CancellationToken cancellationToken = default)
        {
            return await ConnectAndGetInfoAsync(pluginPath, cancellationToken);
        }

        private async Task<DistributionServiceInformation> ConnectAndGetInfoAsync(string pluginPath, CancellationToken cancellationToken)
        {
            await _distributionClient.ConnectAsync(pluginPath, cancellationToken);
            try
            {
                DistributionServiceInformation serviceInfo = await _distributionClient.GetServiceInformationAsync(cancellationToken);
                return serviceInfo;
            }
            finally
            {
                await _distributionClient.DisposeAsync();
            }
        }
    }
}
