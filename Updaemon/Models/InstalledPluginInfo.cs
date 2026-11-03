namespace Updaemon.Models
{
    /// <summary>
    /// Represents an installed distribution plugin.
    /// </summary>
    public class InstalledPluginInfo
    {
        /// <summary>
        /// User-assigned alias for the plugin.
        /// </summary>
        public string Alias { get; set; } = string.Empty;

        /// <summary>
        /// Path to the plugin executable.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Where the plugin was installed from, which decides how it is updated.
        /// </summary>
        public required PluginSource Source { get; init; }
    }
}

