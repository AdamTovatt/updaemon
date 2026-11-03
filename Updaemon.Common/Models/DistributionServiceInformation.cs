namespace Updaemon.Common.Models
{
    /// <summary>
    /// Static metadata about a distribution service plugin.
    /// </summary>
    public class DistributionServiceInformation
    {
        public string FullName { get; set; } = string.Empty;

        public string DefaultAlias { get; set; } = string.Empty;

        public string? Description { get; set; }
            = null;

        public string Version { get; set; } = string.Empty;

        public List<DistributionSecretInfo> Secrets { get; set; } = new List<DistributionSecretInfo>();

        /// <summary>
        /// The plugin protocol version this plugin was built with. It comes from the
        /// Updaemon.Common the plugin is compiled against, so a plugin cannot misreport it.
        /// </summary>
        public int ProtocolVersion => PluginProtocol.Version;
    }
}

