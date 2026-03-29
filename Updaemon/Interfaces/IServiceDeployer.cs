using Updaemon.Models;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Handles the shared deploy pipeline: download, find executable, set permissions, create symlink.
    /// </summary>
    public interface IServiceDeployer
    {
        /// <summary>
        /// Returns the conventional symlink path for a service (e.g. /opt/{localName}/current).
        /// </summary>
        string GetSymlinkPath(string localName);

        /// <summary>
        /// Reads the current symlink target for a service, or null if not initialized.
        /// </summary>
        Task<string?> ReadCurrentTargetAsync(string localName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a version, detects the executable, sets permissions, and creates/updates the symlink.
        /// Returns the deploy result on success, or null if the executable was not found.
        /// </summary>
        Task<DeployResult?> DeployVersionAsync(
            RegisteredService service,
            Version version,
            IDistributionServiceClient distributionClient,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Cleans up artifacts created by a deploy (version directory and symlink). Best-effort.
        /// </summary>
        Task CleanupDeployAsync(DeployResult result, CancellationToken cancellationToken = default);
    }
}
