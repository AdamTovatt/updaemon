using System.Text.Json.Serialization;

namespace Updaemon.Models
{
    /// <summary>
    /// Represents an asset in a GitHub release.
    /// </summary>
    public class GitHubAsset
    {
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}

