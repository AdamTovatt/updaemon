using System.Net;
using Updaemon.Common.Utilities;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// A GitHub release asset at <c>.../releases/download/&lt;tag&gt;/&lt;asset&gt;</c> is checked against
    /// <c>.../releases/download/&lt;tag&gt;/SHA256SUMS</c>, which every updaemon release carries.
    /// </summary>
    public class ReleaseChecksums : IReleaseChecksums
    {
        private readonly HttpClient _httpClient;

        public ReleaseChecksums(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task VerifyAsync(string assetUrl, string filePath, CancellationToken cancellationToken = default)
        {
            Uri asset = new Uri(assetUrl);
            string assetName = Path.GetFileName(asset.LocalPath);
            using (HttpResponseMessage response = await _httpClient.GetAsync(new Uri(asset, Sha256Sums.AssetName), cancellationToken))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new ChecksumException($"{assetName} cannot be verified: its release has no {Sha256Sums.AssetName}.");
                }

                response.EnsureSuccessStatusCode();
                string sums = await response.Content.ReadAsStringAsync(cancellationToken);
                await Sha256Sums.Parse(sums).VerifyAsync(filePath, assetName, cancellationToken);
            }
        }
    }
}
