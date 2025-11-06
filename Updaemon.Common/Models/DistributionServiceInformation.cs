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
    }
}

