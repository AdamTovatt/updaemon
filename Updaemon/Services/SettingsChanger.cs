using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>Changes an installed app's settings or arguments: what set, unset and set-args share.</summary>
    public class SettingsChanger
    {
        private readonly AppPreparer _preparer;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IServiceManager _serviceManager;
        private readonly IOutputWriter _outputWriter;

        public SettingsChanger(AppPreparer preparer, IServiceDeployer serviceDeployer, IServiceManager serviceManager, IOutputWriter outputWriter)
        {
            _preparer = preparer;
            _serviceDeployer = serviceDeployer;
            _serviceManager = serviceManager;
            _outputWriter = outputWriter;
        }

        /// <summary>Ends what an earlier run left unfinished, then hands out a change to edit. Saves nothing of it.</summary>
        /// <exception cref="ServiceManagerUnreachableException">The app has a service, and the service manager cannot be reached.</exception>
        public async Task<SettingsChange> BeginAsync(InstalledApp app, CancellationToken cancellationToken = default)
        {
            PreparedApp prepared = await _preparer.PrepareAsync(app, cancellationToken);
            ReachableServiceManager reachable = await _serviceDeployer.ReachServiceManagerAsync(app.Name, cancellationToken: cancellationToken);
            AppManifest manifest = await _serviceDeployer.GetCurrentManifestAsync(app.Name, cancellationToken);
            return new SettingsChange(await prepared.BeginChangeAsync(cancellationToken), manifest, reachable);
        }

        /// <summary>
        /// Saves the change, generates the app's files from it and restarts the service if it is enabled, running
        /// or not: one crash-looping between automatic restarts would otherwise keep its old unit until the next
        /// update. The change stands whatever the service then does.
        /// </summary>
        /// <returns>False when the restarted service did not pass the health check, as after an update.</returns>
        public async Task<bool> ApplyAsync(SettingsChange settings, CancellationToken cancellationToken = default)
        {
            AppChange change = settings.Change;
            string appName = change.After.Name;
            bool restarts = _serviceDeployer.HasService(appName) && await _serviceManager.IsEnabledAsync(appName, cancellationToken);
            UnfinishedChange.CarryOut left = new UnfinishedChange.CarryOut { Restart = restarts };
            if (change.Values.SecretsChanged)
            {
                // They are not in the entry, so the run that is killed after them leaves them without it.
                await change.SaveNoteAsync(left, cancellationToken);
            }

            await change.CommitAsync(left, cancellationToken);
            await _serviceDeployer.RegenerateAsync(change, settings.Reachable, cancellationToken);
            if (!restarts)
            {
                await change.ClearNoteAsync(_outputWriter);
                return true;
            }

            await _serviceManager.RestartServiceAsync(appName, cancellationToken);
            change.ServiceRestarted();
            await change.ClearNoteAsync(_outputWriter);
            _outputWriter.WriteLine($"Restarted {appName}.");
            return await _serviceDeployer.StaysRunningAsync(appName, cancellationToken);
        }
    }

    /// <summary>A change to an app's settings or arguments under way.</summary>
    /// <param name="Manifest">That of the app's current version.</param>
    public sealed record SettingsChange(AppChange Change, AppManifest Manifest, ReachableServiceManager Reachable);
}
