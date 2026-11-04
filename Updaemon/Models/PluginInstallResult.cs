using Updaemon.Common.Models;

namespace Updaemon.Models
{
    /// <summary>
    /// A plugin just installed, as recorded in the config, with what it reported about itself.
    /// </summary>
    public class PluginInstallResult
    {
        public required InstalledPluginInfo Plugin { get; init; }

        public required DistributionServiceInformation ServiceInformation { get; init; }

        /// <summary>
        /// True when a plugin was already installed under the alias and has been replaced.
        /// </summary>
        public required bool ReplacedExisting { get; init; }
    }
}
