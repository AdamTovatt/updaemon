using System.Net;
using System.Text.Json;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Serialization;

namespace Updaemon.Services
{
    /// <summary>
    /// Third-party plugins' GitHub releases. The latest tag is read as <see cref="GitHubLatestRelease"/>
    /// does, with no rate limit; only installing a release asks the API for its assets.
    /// </summary>
    public class GitHubPluginReleases : IGitHubPluginReleases
    {
        private readonly HttpClient _httpClient;
        private readonly HostPlatform _platform;

        public GitHubPluginReleases(HttpClient httpClient, HostPlatform platform)
        {
            _httpClient = httpClient;
            _platform = platform;
        }

        public async Task<string> GetLatestTagAsync(string owner, string repo, CancellationToken cancellationToken = default)
        {
            string? tag;
            try
            {
                tag = await GitHubLatestRelease.ReadTagAsync(_httpClient, owner, repo, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"{owner}/{repo} was not found on GitHub.");
            }

            return tag ?? throw new InvalidOperationException($"{owner}/{repo} has no release on GitHub.");
        }

        public async Task<string> GetBuildUrlAsync(string owner, string repo, string tag, CancellationToken cancellationToken = default)
        {
            GitHubRelease release;
            string url = $"https://api.github.com/repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(tag)}";
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                // The API refuses a request without one.
                request.Headers.UserAgent.ParseAdd("updaemon");
                using (HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync(cancellationToken);
                    release = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Default.GitHubRelease)
                        ?? throw new InvalidOperationException($"GitHub returned no release {tag} of {owner}/{repo}.");
                }
            }

            string ending = $"-{_platform.RuntimeIdentifier}";
            GitHubReleaseAsset[] builds = release.Assets.Where(asset => asset.Name.EndsWith(ending, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (builds is [GitHubReleaseAsset build])
            {
                return build.BrowserDownloadUrl;
            }

            string label = $"the release {tag} of {owner}/{repo}";
            string problem = builds.Length == 0 ? $"No asset in {label} is named" : $"Several assets in {label} are named";
            throw new ReleaseBuildException(
                $"{problem} *{ending}, the plugin build for this machine. Its assets:\n" +
                string.Join("\n", release.Assets.Select(asset => $"  {asset.Name}")));
        }
    }
}
