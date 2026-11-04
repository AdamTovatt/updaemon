using Updaemon.Interfaces;
using Updaemon.Models;

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

        /// <summary>Thrown by the next calls, one each, before <see cref="Failure"/> is considered.</summary>
        public Queue<Exception> NextFailures { get; } = new Queue<Exception>();

        public static string BuildUrl(string owner, string repo, string tag) =>
            $"https://github.com/{owner}/{repo}/releases/download/{tag}/{repo}-linux-arm64";

        public Task<string> GetLatestTagAsync(string owner, string repo, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();
            return Task.FromResult(LatestTags.TryGetValue($"{owner}/{repo}", out string? tag)
                ? tag
                : throw new InvalidOperationException($"{owner}/{repo} has no release on GitHub."));
        }

        public Task<PluginDownload> GetBuildAsync(string owner, string repo, string tag, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();
            Uri build = new Uri(BuildUrl(owner, repo, tag));
            return Task.FromResult(new PluginDownload(build, $"{repo}-linux-arm64", new Uri(build, "SHA256SUMS"), new GitHubFetch(Token: null)));
        }

        private void ThrowIfFailing()
        {
            if (NextFailures.TryDequeue(out Exception? next))
            {
                throw next;
            }

            if (Failure != null)
            {
                throw Failure;
            }
        }
    }
}
