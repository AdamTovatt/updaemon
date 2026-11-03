using Updaemon.Models;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// The GitHub releases third-party plugins are installed from (<c>dist-install github:&lt;owner&gt;/&lt;repo&gt;</c>).
    /// A refusal for want of a token throws <see cref="Common.Utilities.CredentialNeededException"/>.
    /// </summary>
    public interface IGitHubPluginReleases
    {
        /// <summary>The tag of the repository's latest release.</summary>
        /// <exception cref="InvalidOperationException">The repository has no release, or a token cannot read it.</exception>
        /// <exception cref="HttpRequestException">GitHub could not be reached.</exception>
        Task<string> GetLatestTagAsync(string owner, string repo, CancellationToken cancellationToken = default);

        /// <summary>Release <paramref name="tag"/>'s one <c>*-&lt;rid&gt;</c> asset for this machine, with the release's SHA256SUMS.</summary>
        /// <exception cref="Models.ReleaseBuildException">No asset, or several, is named for this machine; the message lists the release's assets.</exception>
        /// <exception cref="Common.Utilities.ChecksumException">The release has no SHA256SUMS.</exception>
        /// <exception cref="InvalidOperationException">The repository has no such release.</exception>
        /// <exception cref="HttpRequestException">GitHub could not be reached.</exception>
        Task<PluginDownload> GetBuildAsync(string owner, string repo, string tag, CancellationToken cancellationToken = default);
    }
}
