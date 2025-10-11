using System.Net;
using System.Text.Json;
using Updaemon.GithubDistributionService.Interfaces;
using Updaemon.GithubDistributionService.Models;
using Updaemon.GithubDistributionService.Serialization;

namespace Updaemon.GithubDistributionService.Services
{
    /// <summary>
    /// Client for interacting with the GitHub API.
    /// </summary>
    public class GithubApiClient : IGithubApiClient
    {
        private readonly HttpClient _httpClient;

        public GithubApiClient()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Updaemon-GithubDistributionService");
        }

        public async Task<GithubRelease?> GetLatestReleaseAsync(
            string owner,
            string repo,
            string? token,
            CancellationToken cancellationToken = default)
        {
            string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Add("Authorization", $"Bearer {token}");
            }

            HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            string jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            GithubRelease? release = JsonSerializer.Deserialize(
                jsonContent,
                GithubJsonContext.Default.GithubRelease);

            return release;
        }

        public async Task DownloadAssetAsync(
            string url,
            string targetFilePath,
            string? token,
            CancellationToken cancellationToken = default)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Add("Authorization", $"Bearer {token}");
            }

            HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

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

