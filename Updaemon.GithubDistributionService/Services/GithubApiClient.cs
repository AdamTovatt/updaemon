using System.Text.Json;
using Updaemon.Common.Utilities;
using Updaemon.GithubDistributionService.Interfaces;
using Updaemon.GithubDistributionService.Models;
using Updaemon.GithubDistributionService.Serialization;

namespace Updaemon.GithubDistributionService.Services
{
    /// <summary>
    /// Client for the GitHub API, through <see cref="GitHubAccess"/>: a refusal for want of a token
    /// (rejected, rate limited, or a repository not found without one) throws a <see cref="CredentialNeededException"/>.
    /// </summary>
    public class GithubApiClient : IGithubApiClient
    {
        private readonly HttpClient _httpClient;

        public GithubApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<GithubRelease?> GetLatestReleaseAsync(
            string owner,
            string repo,
            string? token,
            CancellationToken cancellationToken = default)
        {
            using (HttpResponseMessage? response = await GitHubAccess.GetReleaseAsync(_httpClient, owner, repo, "latest", token, cancellationToken))
            {
                if (response == null)
                {
                    return null;
                }

                string jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Deserialize(jsonContent, GithubJsonContext.Default.GithubRelease);
            }
        }

        public async Task DownloadReleaseAssetAsync(
            string owner,
            string repo,
            long assetId,
            string targetFilePath,
            string? token,
            CancellationToken cancellationToken = default)
        {
            using (HttpResponseMessage response = await GitHubAccess.GetAssetAsync(_httpClient, AssetAddress(owner, repo, assetId), token, cancellationToken))
            {
                await SaveAsync(response, targetFilePath, cancellationToken);
            }
        }

        public async Task<string> ReadReleaseAssetTextAsync(
            string owner,
            string repo,
            long assetId,
            string? token,
            CancellationToken cancellationToken = default)
        {
            using (HttpResponseMessage response = await GitHubAccess.GetAssetAsync(_httpClient, AssetAddress(owner, repo, assetId), token, cancellationToken))
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
        }

        public async Task DownloadAssetAsync(
            string url,
            string targetFilePath,
            string? token,
            CancellationToken cancellationToken = default)
        {
            using (HttpResponseMessage response = await GitHubAccess.GetAssetAsync(_httpClient, new Uri(url), token, cancellationToken))
            {
                await SaveAsync(response, targetFilePath, cancellationToken);
            }
        }

        private static Uri AssetAddress(string owner, string repo, long assetId) =>
            new Uri($"https://api.github.com/repos/{owner}/{repo}/releases/assets/{assetId}");

        private static async Task SaveAsync(HttpResponseMessage response, string targetFilePath, CancellationToken cancellationToken)
        {
            string? directory = Path.GetDirectoryName(targetFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            using (FileStream fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
            {
                await contentStream.CopyToAsync(fileStream, cancellationToken);
            }
        }
    }
}
