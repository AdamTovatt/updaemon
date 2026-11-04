namespace Updaemon.Models
{
    /// <summary>The part of the GitHub API's release object updaemon reads.</summary>
    public sealed class GitHubRelease
    {
        public string TagName { get; init; } = string.Empty;

        // A setter, not init: see AppManifest.Settings.
        public List<GitHubReleaseAsset> Assets { get; set; } = new List<GitHubReleaseAsset>();
    }

    public sealed class GitHubReleaseAsset
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>The asset through the API, which serves a private repository's assets to a token.</summary>
        public string Url { get; init; } = string.Empty;

        public string BrowserDownloadUrl { get; init; } = string.Empty;
    }
}
