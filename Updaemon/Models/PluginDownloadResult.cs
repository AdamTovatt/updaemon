using Updaemon.Common.Models;

namespace Updaemon.Models
{
    /// <summary>
    /// Result of downloading and inspecting a distribution plugin.
    /// </summary>
    public class PluginDownloadResult
    {
        /// <summary>
        /// Path to the temporary downloaded plugin binary.
        /// The caller is responsible for moving or deleting this file.
        /// </summary>
        public required string TempFilePath { get; init; }

        /// <summary>
        /// Service information retrieved from the downloaded plugin.
        /// </summary>
        public required DistributionServiceInformation ServiceInformation { get; init; }
    }
}
