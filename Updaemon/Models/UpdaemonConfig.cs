namespace Updaemon.Models
{
    /// <summary>
    /// Root configuration for updaemon stored in /var/lib/updaemon/config.json
    /// </summary>
    public class UpdaemonConfig
    {
        /// <summary>
        /// Dictionary of installed distribution plugins, keyed by alias.
        /// </summary>
        public Dictionary<string, InstalledPluginInfo> InstalledPlugins { get; set; } = new Dictionary<string, InstalledPluginInfo>();

        /// <summary>
        /// List of registered services.
        /// </summary>
        public List<RegisteredService> Services { get; set; } = new List<RegisteredService>();

        /// <summary>
        /// Total number of release versions to retain per service after a successful deployment.
        /// Includes the currently-deployed version. Minimum effective value is 1.
        /// </summary>
        public int ReleaseRetentionCount { get; set; } = 5;
    }
}

