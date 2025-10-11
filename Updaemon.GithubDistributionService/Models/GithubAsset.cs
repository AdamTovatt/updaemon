using System.Text.Json.Serialization;

namespace Updaemon.GithubDistributionService.Models
{
    /// <summary>
    /// Represents an asset in a GitHub release.
    /// </summary>
    public class GithubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}

