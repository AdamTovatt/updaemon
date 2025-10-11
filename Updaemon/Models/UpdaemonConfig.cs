namespace Updaemon.Models
{
    /// <summary>
    /// Root configuration for updaemon stored in /var/lib/updaemon/config.json
    /// </summary>
    public class UpdaemonConfig
    {
        /// <summary>
        /// Path to the active distribution service plugin executable.
        /// </summary>
        public string? DistributionPluginPath { get; set; }

        /// <summary>
        /// List of registered services.
        /// </summary>
        public List<RegisteredService> Services { get; set; } = new List<RegisteredService>();
    }
}

