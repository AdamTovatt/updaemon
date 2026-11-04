using System.Net;
using System.Text.Json;
using Updaemon.Common.Utilities;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Serialization;

namespace Updaemon.Services
{
    /// <summary>
    /// Third-party plugins' GitHub releases. They use the GitHub token of the 'github' plugin
    /// (<see cref="TokenPluginAlias"/>), which a private repository needs. Without one, the latest tag
    /// is read as <see cref="GitHubLatestRelease"/> does, with no rate limit; with one, from the API,
    /// as that redirect takes no token. Installing a release always asks the API for its assets.
    /// </summary>
    public class GitHubPluginReleases : IGitHubPluginReleases
    {
        /// <summary>The plugin whose <see cref="GitHubAccess.TokenSecret"/> github: plugins use too.</summary>
        public const string TokenPluginAlias = "github";

        private readonly HttpClient _httpClient;
        private readonly HostPlatform _platform;
        private readonly ISecretsManager _secretsManager;

        public GitHubPluginReleases(HttpClient httpClient, HostPlatform platform, ISecretsManager secretsManager)
        {
            _httpClient = httpClient;
            _platform = platform;
            _secretsManager = secretsManager;
        }

        public async Task<string> GetLatestTagAsync(string owner, string repo, CancellationToken cancellationToken = default)
        {
            string? token = await TokenAsync(cancellationToken);
            string? tag;
            if (GitHubAccess.HasToken(token))
            {
                tag = (await ReadReleaseAsync(owner, repo, "latest", token, cancellationToken))?.TagName;
            }
            else
            {
                try
                {
                    tag = await GitHubLatestRelease.ReadTagAsync(_httpClient, owner, repo, cancellationToken);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    throw GitHubAccess.RepositoryNotFound(owner, repo, hasToken: false);
                }
            }

            return tag ?? throw new InvalidOperationException($"{owner}/{repo} has no release on GitHub.");
        }

        public async Task<PluginDownload> GetBuildAsync(string owner, string repo, string tag, CancellationToken cancellationToken = default)
        {
            string? token = await TokenAsync(cancellationToken);
            GitHubRelease release = await ReadReleaseAsync(owner, repo, $"tags/{Uri.EscapeDataString(tag)}", token, cancellationToken)
                ?? throw new InvalidOperationException($"{owner}/{repo} has no release {tag} on GitHub.");

            string ending = $"-{_platform.RuntimeIdentifier}";
            GitHubReleaseAsset[] builds = release.Assets.Where(asset => asset.Name.EndsWith(ending, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (builds is not [GitHubReleaseAsset build])
            {
                string label = $"the release {tag} of {owner}/{repo}";
                string problem = builds.Length == 0 ? $"No asset in {label} is named" : $"Several assets in {label} are named";
                throw new ReleaseBuildException(
                    $"{problem} *{ending}, the plugin build for this machine. Its assets:\n" +
                    string.Join("\n", release.Assets.Select(asset => $"  {asset.Name}")));
            }

            GitHubReleaseAsset sums = release.Assets.FirstOrDefault(asset => asset.Name == Sha256Sums.AssetName)
                ?? throw new ChecksumException($"{build.Name} cannot be verified: its release has no {Sha256Sums.AssetName}.");

            // A private repository's assets are served to a token through the API only.
            bool viaApi = GitHubAccess.HasToken(token);
            Uri AddressOf(GitHubReleaseAsset asset) => new Uri(viaApi ? asset.Url : asset.BrowserDownloadUrl);
            return new PluginDownload(AddressOf(build), build.Name, AddressOf(sums), new GitHubFetch(viaApi ? token : null));
        }

        private Task<string?> TokenAsync(CancellationToken cancellationToken) =>
            _secretsManager.GetSecretAsync(TokenPluginAlias, GitHubAccess.TokenSecret, cancellationToken);

        /// <summary>Release <paramref name="which"/> (<c>latest</c> or <c>tags/&lt;tag&gt;</c>); null when the repository has no such release.</summary>
        private async Task<GitHubRelease?> ReadReleaseAsync(string owner, string repo, string which, string? token, CancellationToken cancellationToken)
        {
            using (HttpResponseMessage? response = await GitHubAccess.GetReleaseAsync(_httpClient, owner, repo, which, token, cancellationToken))
            {
                if (response == null)
                {
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Deserialize(json, GitHubApiJsonContext.Default.GitHubRelease)
                    ?? throw new InvalidOperationException($"GitHub returned no release for {owner}/{repo}.");
            }
        }
    }
}
