namespace Updaemon.Common.Models
{
    /// <summary>
    /// Describes a secret used by a distribution service plugin.
    /// </summary>
    public class DistributionSecretInfo
    {
        public string Name { get; set; } = string.Empty;

        public bool IsRequired { get; set; }
            = false;

        public string? Description { get; set; }
            = null;
    }
}

