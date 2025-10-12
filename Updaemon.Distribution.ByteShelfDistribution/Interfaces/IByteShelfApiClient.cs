using ByteShelfCommon;

namespace Updaemon.Distribution.ByteShelfDistribution.Interfaces
{
    /// <summary>
    /// Client interface for interacting with the ByteShelf API.
    /// </summary>
    public interface IByteShelfApiClient
    {
        /// <summary>
        /// Finds a subtenant by its display name.
        /// </summary>
        /// <param name="displayName">The display name of the subtenant to find.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>A tuple containing the subtenant ID and info, or null if not found.</returns>
        Task<(string id, TenantInfoResponse info)?> GetSubTenantByDisplayNameAsync(
            string displayName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all version subtenants under an app subtenant.
        /// </summary>
        /// <param name="appSubtenantId">The ID of the app subtenant.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>Dictionary of version subtenant IDs to their information.</returns>
        Task<Dictionary<string, TenantInfoResponse>> GetVersionSubTenantsAsync(
            string appSubtenantId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds a version subtenant that matches the specified version.
        /// </summary>
        /// <param name="appSubtenantId">The ID of the app subtenant.</param>
        /// <param name="version">The version to find.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>A tuple containing the subtenant ID and info, or null if not found.</returns>
        Task<(string id, TenantInfoResponse info)?> GetVersionSubTenantByVersionAsync(
            string appSubtenantId,
            Version version,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all files in a specific subtenant.
        /// </summary>
        /// <param name="subtenantId">The ID of the subtenant.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>Collection of file metadata.</returns>
        Task<IEnumerable<ShelfFileMetadata>> GetFilesInSubTenantAsync(
            string subtenantId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a file from a subtenant to the specified target path.
        /// </summary>
        /// <param name="subtenantId">The ID of the subtenant containing the file.</param>
        /// <param name="fileId">The ID of the file to download.</param>
        /// <param name="targetPath">The local path where the file should be saved.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        Task DownloadFileAsync(
            string subtenantId,
            Guid fileId,
            string targetPath,
            CancellationToken cancellationToken = default);
    }
}

