using Updaemon.Configuration;
using Updaemon.Models;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Downloads, activates, restarts and removes apps.
    /// </summary>
    public interface IServiceDeployer
    {
        /// <summary>The manifest of the app's current version.</summary>
        /// <exception cref="InvalidOperationException">The app has no current version; the message says how to recover.</exception>
        /// <exception cref="ManifestException">The current version's manifest is invalid.</exception>
        Task<AppManifest> GetCurrentManifestAsync(string appName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a version into its version directory, resolves its manifest (the release's own, else the
        /// app's from install flags) and makes it ready to run. Leaves no directory if anything fails.
        /// </summary>
        /// <exception cref="ManifestException">The release's manifest is invalid.</exception>
        /// <exception cref="MissingManifestException">Neither the release nor the app has a manifest.</exception>
        /// <exception cref="InvalidOperationException">It is the current version already, or the executable its manifest names is not in it.</exception>
        Task<DeployableVersion> StageAsync(InstalledApp app, Version version, IDistributionServiceClient distributionClient, CancellationToken cancellationToken = default);

        /// <summary>
        /// Before any change to an app is written: checks the service manager can be reached when the app
        /// has a service (<see cref="HasService"/>) or <paramref name="next"/>, the version about to be
        /// activated, has one. Every method below that may write a unit, or start or stop a service, takes
        /// what this returns.
        /// </summary>
        /// <exception cref="ServiceManagerUnreachableException">It cannot be reached.</exception>
        Task<ReachableServiceManager> ReachServiceManagerAsync(string appName, AppManifest? next = null, CancellationToken cancellationToken = default);

        /// <summary>Whether the app has a service now: its unit or plist is there.</summary>
        bool HasService(string appName);

        /// <summary>Makes the version's executable executable.</summary>
        /// <exception cref="InvalidOperationException">The executable its manifest names is not in it.</exception>
        Task MakeExecutableAsync(string appName, DeployableVersion version, CancellationToken cancellationToken = default);

        /// <summary>A retained version with the manifest in its directory.</summary>
        /// <exception cref="InvalidOperationException">The version is not kept; the message lists the kept ones.</exception>
        Task<DeployableVersion> OpenRetainedAsync(string appName, Version version, CancellationToken cancellationToken = default);

        /// <summary>The current version with the manifest in its directory.</summary>
        /// <exception cref="InvalidOperationException">The app has no current version; the message says how to recover.</exception>
        /// <exception cref="ManifestException">The current version's manifest is invalid.</exception>
        Task<DeployableVersion> OpenCurrentAsync(string appName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Makes the staged or retained version current and regenerates the app's settings file, command
        /// wrapper and service unit from its manifest. Does not start or restart the service. What is
        /// current when this fails partway is for the caller to put back (see <see cref="Services.AppSwitcher"/>).
        /// </summary>
        /// <remarks>
        /// Takes no <see cref="AppChange"/>, as a first install, a conversion from 0.x and putting a version
        /// back have none. A command that switches an installed app goes through <see cref="Services.AppSwitcher"/>.
        /// </remarks>
        /// <param name="givenUp">
        /// For a version that is put back: the manifest of the one it was switched to, whose files go too,
        /// whichever version is current. Null when there is none.
        /// </param>
        Task ActivateAsync(InstalledApp app, DeployableVersion version, ReachableServiceManager reachable, AppManifest? givenUp = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Regenerates the app's settings file, command wrapper and service unit for its current version and
        /// <see cref="AppChange.After"/>.
        /// </summary>
        Task RegenerateAsync(AppChange change, ReachableServiceManager reachable, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables and starts a newly installed service, then runs the health check (<see cref="StaysRunningAsync"/>).
        /// </summary>
        Task<bool> StartAsync(string appName, ReachableServiceManager reachable, CancellationToken cancellationToken = default);

        /// <summary>
        /// Restarts the service, also one that is stopped, failed or crash-looping, then runs the health check as
        /// <see cref="StartAsync"/> does.
        /// </summary>
        Task<bool> RestartAsync(string appName, ReachableServiceManager reachable, CancellationToken cancellationToken = default);

        /// <summary>
        /// Restarts the service for <paramref name="change"/>, which then owes no restart. That is saved here, before
        /// the health check, which is the caller's next step: cancelling it leaves nothing to make up for.
        /// </summary>
        Task RestartForAsync(AppChange change, ReachableServiceManager reachable, CancellationToken cancellationToken = default);

        /// <summary>
        /// The health check, right after a (re)start: whether the service came up and kept running for its
        /// current version's <see cref="ServiceSpec.HealthCheck"/> without the service manager starting it again.
        /// </summary>
        Task<bool> StaysRunningAsync(string appName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops and deletes the app's unit, its command wrapper, all its versions, settings and secrets;
        /// with <paramref name="purge"/> also its data directory. Leaves the config to the caller.
        /// </summary>
        Task RemoveAsync(InstalledApp app, bool purge, ReachableServiceManager reachable, CancellationToken cancellationToken = default);
    }
}
