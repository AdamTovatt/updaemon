using Updaemon.Common.Models;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IPluginDownloader for testing.
    /// </summary>
    public class MockPluginDownloader : IPluginDownloader
    {
        /// <summary>
        /// The contents of every binary this mock "downloads".
        /// </summary>
        public static readonly byte[] PluginBytes = { 0x7F, 0x45, 0x4C, 0x46 };

        public List<string> MethodCalls { get; } = new List<string>();
        public DistributionServiceInformation? ServiceInformation { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        /// <summary>Each download as asked for.</summary>
        public List<PluginDownload> Downloads { get; } = new List<PluginDownload>();

        public Task<PluginDownloadResult> DownloadAndInspectAsync(PluginDownload download, string targetDirectory, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"DownloadAndInspectAsync:{download.Build}");
            Downloads.Add(download);

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            // Create actual temp file so File.Move works in the command
            Directory.CreateDirectory(targetDirectory);
            string tempPath = Path.Combine(targetDirectory, $"temp_{Guid.NewGuid():N}");
            File.WriteAllBytes(tempPath, PluginBytes);

            DistributionServiceInformation info = ServiceInformation ?? new DistributionServiceInformation
            {
                FullName = "Mock Distribution Service",
                DefaultAlias = "mock",
                Description = "Mock distribution service for testing",
                Version = "1.0.0",
                Secrets = new List<DistributionSecretInfo>(),
            };

            return Task.FromResult(new PluginDownloadResult
            {
                TempFilePath = tempPath,
                ServiceInformation = info,
            });
        }
    }
}
