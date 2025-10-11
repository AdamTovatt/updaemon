namespace Updaemon.Models
{
    /// <summary>
    /// Optional configuration file (updaemon.json) that can be included in published apps
    /// to provide hints for updaemon during installation and updates.
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// Relative path to the executable within the version directory.
        /// If not specified, updaemon will search for an executable matching the service name.
        /// </summary>
        public string? ExecutablePath { get; set; }
    }
}

