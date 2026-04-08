namespace Updaemon.Models
{
    /// <summary>
    /// Represents a service registered with updaemon.
    /// </summary>
    public class RegisteredService
    {
        /// <summary>
        /// Local name used for the directory at /opt/{LocalName}/ and (for services) the systemd unit name.
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

        /// <summary>
        /// Alias of the distribution plugin to use for this service.
        /// </summary>
        public string DistributionPluginAlias { get; set; } = string.Empty;

        /// <summary>
        /// The type of this entry: Service (systemd managed) or Cli (PATH symlink only).
        /// Defaults to Service for backwards compatibility with existing configs.
        /// </summary>
        public ServiceType ServiceType { get; set; } = ServiceType.Service;
    }
}

