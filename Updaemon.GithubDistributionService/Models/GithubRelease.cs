using System.Text.Json.Serialization;

namespace Updaemon.GithubDistributionService.Models
{
    /// <summary>
    /// Represents a GitHub release.
    /// </summary>
    public class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public GithubAsset[] Assets { get; set; } = Array.Empty<GithubAsset>();
    }
}

