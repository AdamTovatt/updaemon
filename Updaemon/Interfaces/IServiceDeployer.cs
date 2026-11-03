using Updaemon.Models;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Downloads, activates, restarts and removes apps.
    /// </summary>
    public interface IServiceDeployer
    {
        /// <summary>The version the app's <c>current</c> link points at.</summary>
        /// <exception cref="InvalidOperationException">The app has no current version; the message says how to recover.</exception>
        Task<Version> GetCurrentVersionAsync(string appName, CancellationToken cancellationToken = default);

        /// <summary>The manifest of the app's current version.</summary>
        /// <exception cref="InvalidOperationException">The app has no current version; the message says how to recover.</exception>
        /// <exception cref="ManifestException">The current version's manifest is invalid.</exception>
        Task<AppManifest> GetCurrentManifestAsync(string appName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a version into its version directory and resolves its manifest: the release's own,
        /// else the app's manifest from install flags. Deletes the directory if anything fails.
        /// </summary>
        /// <exception cref="ManifestException">The release's manifest is invalid.</exception>
        /// <exception cref="MissingManifestException">Neither the release nor the app has a manifest.</exception>
        Task<DeployableVersion> StageAsync(InstalledApp app, Version version, IDistributionServiceClient distributionClient, CancellationToken cancellationToken = default);

        /// <summary>Deletes a staged version that will not be activated.</summary>
        void Discard(DeployableVersion staged);

        /// <summary>The versions kept in the app's directory, the current one included, oldest first.</summary>
        IReadOnlyList<Version> GetRetainedVersions(string appName);

        /// <summary>A retained version with the manifest in its directory, ready to activate again.</summary>
        /// <exception cref="InvalidOperationException">The version is not kept; the message lists the kept ones.</exception>
        Task<DeployableVersion> OpenRetainedAsync(string appName, Version version, CancellationToken cancellationToken = default);

        /// <summary>
        /// Makes the staged version current and regenerates the app's settings file, command wrapper and
        /// service unit from its manifest. Does not start or restart the service. If generating fails,
        /// the previous version is made current again before the exception propagates.
        /// </summary>
        Task ActivateAsync(InstalledApp app, DeployableVersion version, CancellationToken cancellationToken = default);

        /// <summary>
        /// After a change to the app's settings or arguments: regenerates its settings file, command
        /// wrapper and service unit for its current version, and restarts the service if it is running.
        /// </summary>
        Task ApplySettingsAsync(InstalledApp app, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables and starts a newly installed service, then runs the health check: returns whether it
        /// came up and kept running for the <see cref="Configuration.HealthCheckWait"/> without the
        /// service manager starting it again.
        /// </summary>
        Task<bool> StartAsync(string appName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Restarts the service, or starts it if it is not running, then runs the health check as
        /// <see cref="StartAsync"/> does.
        /// </summary>
        Task<bool> RestartAsync(string appName, CancellationToken cancellationToken = default);

        /// <summary>Restarts the service if it is running. Returns whether it did.</summary>
        Task<bool> RestartIfRunningAsync(string appName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops and deletes the app's unit, its command wrapper, all its versions, settings and secrets;
        /// with <paramref name="purge"/> also its data directory. Leaves the config to the caller.
        /// </summary>
        Task RemoveAsync(InstalledApp app, bool purge, CancellationToken cancellationToken = default);

        /// <summary>Deletes the oldest version directories beyond <paramref name="retentionCount"/>, never the current one.</summary>
        Task PruneOldVersionsAsync(string appName, int retentionCount, CancellationToken cancellationToken = default);
    }
}
