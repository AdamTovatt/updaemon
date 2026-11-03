namespace Updaemon.Interfaces
{
    /// <summary>
    /// The GitHub releases third-party plugins are installed from (<c>dist-install github:&lt;owner&gt;/&lt;repo&gt;</c>).
    /// </summary>
    public interface IGitHubPluginReleases
    {
        /// <summary>The tag of the repository's latest release.</summary>
        /// <exception cref="InvalidOperationException">The repository has no release.</exception>
        /// <exception cref="HttpRequestException">GitHub could not be reached.</exception>
        Task<string> GetLatestTagAsync(string owner, string repo, CancellationToken cancellationToken = default);

        /// <summary>The download address of release <paramref name="tag"/>'s one <c>*-&lt;rid&gt;</c> asset for this machine.</summary>
        /// <exception cref="Models.ReleaseBuildException">No asset, or several, is named for this machine; the message lists the release's assets.</exception>
        /// <exception cref="HttpRequestException">GitHub could not be reached, or has no such release.</exception>
        Task<string> GetBuildUrlAsync(string owner, string repo, string tag, CancellationToken cancellationToken = default);
    }
}
