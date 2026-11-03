using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// GitHub repositories' releases without the network: <see cref="LatestTags"/> by "owner/repo",
    /// and each release's build at <see cref="BuildUrl"/>.
    /// </summary>
    public class MockGitHubPluginReleases : IGitHubPluginReleases
    {
        public Dictionary<string, string> LatestTags { get; } = new Dictionary<string, string>();

        /// <summary>Thrown by every call when set.</summary>
        public Exception? Failure { get; set; }

        public static string BuildUrl(string owner, string repo, string tag) =>
            $"https://github.com/{owner}/{repo}/releases/download/{tag}/{repo}-linux-arm64";

        public Task<string> GetLatestTagAsync(string owner, string repo, CancellationToken cancellationToken = default)
        {
            if (Failure != null)
            {
                throw Failure;
            }

            return Task.FromResult(LatestTags.TryGetValue($"{owner}/{repo}", out string? tag)
                ? tag
                : throw new InvalidOperationException($"{owner}/{repo} has no release on GitHub."));
        }

        public Task<string> GetBuildUrlAsync(string owner, string repo, string tag, CancellationToken cancellationToken = default)
        {
            return Failure != null ? throw Failure : Task.FromResult(BuildUrl(owner, repo, tag));
        }
    }
}
