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
        public List<string> MethodCalls { get; } = new List<string>();
        public DistributionServiceInformation? ServiceInformation { get; set; }
        public Dictionary<string, DistributionServiceInformation> LocalServiceInformationByPath { get; } = new Dictionary<string, DistributionServiceInformation>();
        public Exception? ExceptionToThrow { get; set; }

        public Task<PluginDownloadResult> DownloadAndInspectAsync(string downloadUrl, string targetDirectory, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"DownloadAndInspectAsync:{downloadUrl}");

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            // Create actual temp file so File.Move works in the command
            Directory.CreateDirectory(targetDirectory);
            string tempPath = Path.Combine(targetDirectory, $"temp_{Guid.NewGuid():N}");
            File.WriteAllBytes(tempPath, new byte[] { 0x7F, 0x45, 0x4C, 0x46 });

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

        public Task<DistributionServiceInformation> InspectLocalAsync(string pluginPath, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"InspectLocalAsync:{pluginPath}");

            if (LocalServiceInformationByPath.TryGetValue(pluginPath, out DistributionServiceInformation? info))
            {
                return Task.FromResult(info);
            }

            return Task.FromResult(ServiceInformation ?? new DistributionServiceInformation
            {
                FullName = "Mock Distribution Service",
                DefaultAlias = "mock",
                Description = "Mock distribution service for testing",
                Version = "1.0.0",
                Secrets = new List<DistributionSecretInfo>(),
            });
        }
    }
}
