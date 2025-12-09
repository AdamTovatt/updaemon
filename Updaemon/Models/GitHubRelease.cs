using System.Text.Json.Serialization;

namespace Updaemon.Models
{
    /// <summary>
    /// Represents a GitHub release for self-update purposes.
    /// </summary>
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
    }
}

