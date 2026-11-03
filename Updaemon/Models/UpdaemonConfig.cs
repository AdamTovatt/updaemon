namespace Updaemon.Models
{
    /// <summary>
    /// Root configuration for one scope, stored in the scope root's config.json.
    /// </summary>
    public class UpdaemonConfig
    {
        /// <summary>The config format this updaemon reads and writes. 0.x configs have no format field.</summary>
        public const int CurrentFormat = 1;

        /// <summary>
        /// Always written as <see cref="CurrentFormat"/>; read-only, so loading never takes it from the file.
        /// <see cref="Configuration.ConfigManager"/> checks the file's value before deserializing.
        /// </summary>
        public int Format => CurrentFormat;

        /// <summary>
        /// Dictionary of installed distribution plugins, keyed by alias.
        /// </summary>
        public Dictionary<string, InstalledPluginInfo> InstalledPlugins { get; set; } = new Dictionary<string, InstalledPluginInfo>();

        /// <summary>
        /// Installed apps.
        /// </summary>
        public List<InstalledApp> Apps { get; set; } = new List<InstalledApp>();

        /// <summary>
        /// Total number of release versions to retain per app after a successful deployment.
        /// Includes the currently-deployed version. Minimum effective value is 1.
        /// </summary>
        public int ReleaseRetentionCount { get; set; } = 5;

        /// <summary>Whether <c>update</c> also updates updaemon itself; set with <c>updaemon self-update on|off</c>.</summary>
        public bool SelfUpdate { get; set; } = true;

        /// <summary>
        /// Whether the update timer was turned off with <c>updaemon timer -</c>, which deletes its files;
        /// <c>timer &lt;interval&gt; --keep</c>, as install.sh runs it, then leaves it off.
        /// </summary>
        public bool TimerTurnedOff { get; set; }
    }
}
