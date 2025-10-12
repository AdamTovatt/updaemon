namespace Updaemon.Models
{
    /// <summary>
    /// Represents a service registered with updaemon.
    /// </summary>
    public class RegisteredService
    {
        /// <summary>
        /// Local name used for systemd service and directory at /opt/{LocalName}/
        /// </summary>
        public string LocalName { get; set; } = string.Empty;

        /// <summary>
        /// Remote name used when querying the distribution service.
        /// </summary>
        public string RemoteName { get; set; } = string.Empty;

        /// <summary>
        /// Optional executable name. When specified, this name is used to search for the executable.
        /// If not specified (null), LocalName is used as the default.
        /// </summary>
        public string? ExecutableName { get; set; }
    }
}

