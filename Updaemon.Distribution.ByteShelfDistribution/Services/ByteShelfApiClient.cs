using ByteShelfClient;
using ByteShelfCommon;
using Updaemon.Common.Utilities;
using Updaemon.Distribution.ByteShelfDistribution.Interfaces;

namespace Updaemon.Distribution.ByteShelfDistribution.Services
{
    /// <summary>
    /// Client for interacting with the ByteShelf API.
    /// Wraps HttpShelfFileProvider to provide distribution-specific operations.
    /// </summary>
    public class ByteShelfApiClient : IByteShelfApiClient
    {
        private readonly IShelfFileProvider _fileProvider;
        private readonly IVersionParser _versionParser;

        public ByteShelfApiClient(IShelfFileProvider fileProvider, IVersionParser versionParser)
        {
            _fileProvider = fileProvider;
            _versionParser = versionParser;
        }

        public async Task<(string id, TenantInfoResponse info)?> GetSubTenantByDisplayNameAsync(
            string displayName,
            CancellationToken cancellationToken = default)
        {
            Dictionary<string, TenantInfoResponse> subtenants = await _fileProvider.GetSubTenantsAsync();

            foreach (KeyValuePair<string, TenantInfoResponse> kvp in subtenants)
            {
                if (kvp.Value.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return (kvp.Key, kvp.Value);
                }
            }

            return null;
        }

        public async Task<Dictionary<string, TenantInfoResponse>> GetVersionSubTenantsAsync(
            string appSubtenantId,
            CancellationToken cancellationToken = default)
        {
            Dictionary<string, TenantInfoResponse> versionSubtenants = await _fileProvider.GetSubTenantsUnderSubTenantAsync(appSubtenantId);
            return versionSubtenants;
        }

        public async Task<(string id, TenantInfoResponse info)?> GetVersionSubTenantByVersionAsync(
            string appSubtenantId,
            Version version,
            CancellationToken cancellationToken = default)
        {
            Dictionary<string, TenantInfoResponse> versionSubtenants = await GetVersionSubTenantsAsync(appSubtenantId, cancellationToken);

            foreach (KeyValuePair<string, TenantInfoResponse> kvp in versionSubtenants)
            {
                Version? parsedVersion = _versionParser.Parse(kvp.Value.DisplayName);
                if (parsedVersion != null && parsedVersion.Equals(version))
                {
                    return (kvp.Key, kvp.Value);
                }
            }

            return null;
        }

        public async Task<IEnumerable<ShelfFileMetadata>> GetFilesInSubTenantAsync(
            string subtenantId,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<ShelfFileMetadata> files = await _fileProvider.GetFilesForTenantAsync(subtenantId);
            return files;
        }

        public async Task DownloadFileAsync(
            string subtenantId,
            Guid fileId,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            ShelfFile file = await _fileProvider.ReadFileForTenantAsync(subtenantId, fileId);

            string? directory = Path.GetDirectoryName(targetPath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (Stream contentStream = file.GetContentStream())
            using (FileStream fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
            {
                await contentStream.CopyToAsync(fileStream, cancellationToken);
            }
        }
    }
}

