using System.Net;
using Updaemon.Common.Utilities;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Checks a downloaded release asset against its release's <c>SHA256SUMS</c>.
    /// </summary>
    public class ReleaseChecksums : IReleaseChecksums
    {
        private readonly HttpClient _httpClient;

        public ReleaseChecksums(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// The <c>SHA256SUMS</c> beside an asset at <c>.../releases/download/&lt;tag&gt;/&lt;asset&gt;</c>,
        /// where every updaemon release has one.
        /// </summary>
        public static Uri BesideAsset(Uri assetUrl) => new Uri(assetUrl, Sha256Sums.AssetName);

        public async Task VerifyAsync(Uri checksumsUrl, string assetName, string filePath, GitHubFetch? gitHub, CancellationToken cancellationToken = default)
        {
            string sums;
            try
            {
                using (HttpResponseMessage response = await ReleaseAsset.GetAsync(_httpClient, checksumsUrl, gitHub, cancellationToken))
                {
                    sums = await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ChecksumException($"{assetName} cannot be verified: its release has no {Sha256Sums.AssetName}.");
            }

            await Sha256Sums.Parse(sums).VerifyAsync(filePath, assetName, cancellationToken);
        }
    }
}
