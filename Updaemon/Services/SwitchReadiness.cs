using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Whether a switch can be made, asked before it writes anything. <see cref="AppSwitcher"/> asks, and so
    /// does a command that needs to know sooner, as before it asks the app's version about its extensions.
    /// </summary>
    public class SwitchReadiness
    {
        private readonly VersionStore _versions;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly AppEntryStore _entryStore;

        public SwitchReadiness(VersionStore versions, IServiceDeployer serviceDeployer, AppEntryStore entryStore)
        {
            _versions = versions;
            _serviceDeployer = serviceDeployer;
            _entryStore = entryStore;
        }

        /// <summary>Makes ready what the switch switches to.</summary>
        /// <exception cref="ServiceManagerUnreachableException">It cannot be reached.</exception>
        /// <exception cref="InvalidOperationException">The app's new version cannot take over, or an extension was accepted by another version than will run.</exception>
        public async Task<ReachableServiceManager> MakeReadyAsync(AppSwitch appSwitch, CancellationToken cancellationToken = default)
        {
            InstalledApp app = appSwitch.App;
            ReachableServiceManager reachable = appSwitch.Next == null
                ? await _serviceDeployer.ReachServiceManagerAsync(app.Name, cancellationToken: cancellationToken)
                : await MakeReadyAsync(app, appSwitch.Next, cancellationToken);
            foreach (AcceptedExtension accepted in appSwitch.Extensions.Select(extension => extension.To).OfType<AcceptedExtension>())
            {
                Version willRun = appSwitch.Next?.Version.Version ?? await _versions.GetCurrentAsync(app.PackageName, cancellationToken);
                if (accepted.HostVersion != willRun)
                {
                    throw new InvalidOperationException($"{accepted.Name} {accepted.Version} was accepted by {app.Name} {accepted.HostVersion}, but {willRun} will run.");
                }
            }

            return reachable;
        }

        /// <summary>Makes a version of the app ready to run, as it is asked about its extensions before it is switched to.</summary>
        /// <remarks>
        /// Does not look for a command name that is taken: that fails the switch, its extensions' with it, which
        /// goes back and is tried again by the next run.
        /// </remarks>
        /// <exception cref="ServiceManagerUnreachableException">It cannot be reached.</exception>
        /// <exception cref="InvalidOperationException">
        /// It needs a setting that has no value, no longer takes the extensions installed into the app or lacks its executable.
        /// </exception>
        public async Task<ReachableServiceManager> MakeReadyAsync(InstalledApp app, NextAppVersion nextVersion, CancellationToken cancellationToken = default)
        {
            (DeployableVersion next, string retryCommand) = nextVersion;
            ReachableServiceManager reachable = await _serviceDeployer.ReachServiceManagerAsync(app.Name, next.Manifest, cancellationToken);
            await _serviceDeployer.MakeExecutableAsync(app.Name, next, cancellationToken);
            Version running = await _versions.GetCurrentAsync(app.PackageName, cancellationToken);
            AppValues values = await _entryStore.LoadValuesAsync(app, cancellationToken);
            values.EnsureComplete(next.Manifest, next.Version, running, retryCommand);
            if (next.Manifest.Extensions == null && app.Extensions.Count > 0)
            {
                string removals = string.Join(", ", app.Extensions.Select(extension => $"'updaemon remove {app.NameOf(extension)}'"));
                throw new InvalidOperationException($"{next.Version} takes no extensions; {running} keeps running. Run {removals}, then '{retryCommand}'.");
            }

            return reachable;
        }
    }
}
