using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// The first step of any command that switches an installed app's versions or applies its settings,
    /// before it writes anything: ends a change that an earlier run left unfinished.
    /// </summary>
    public class AppPreparer
    {
        private readonly SwitchRestorer _restorer;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IServiceManager _serviceManager;
        private readonly AppSettingsStore _settingsStore;
        private readonly IOutputWriter _outputWriter;

        public AppPreparer(
            SwitchRestorer restorer, IServiceDeployer serviceDeployer, IServiceManager serviceManager, AppSettingsStore settingsStore, IOutputWriter outputWriter)
        {
            _restorer = restorer;
            _serviceDeployer = serviceDeployer;
            _serviceManager = serviceManager;
            _settingsStore = settingsStore;
            _outputWriter = outputWriter;
        }

        /// <summary>
        /// Puts back what was current before a change that had not come to stand, and carries out one that had.
        /// No version is skipped, as nothing proved one bad. Needs neither the network nor a plugin. A restart
        /// that fails is only a warning, as the change to come may be what lets the service start; it stays owed.
        /// </summary>
        /// <exception cref="ServiceManagerUnreachableException">There is a change to end, and the service manager it needs cannot be reached.</exception>
        /// <exception cref="InvalidOperationException">It cannot be ended; the message says what is still possible.</exception>
        public async Task<PreparedApp> PrepareAsync(InstalledApp app, CancellationToken cancellationToken = default)
        {
            if (app.Unfinished is not UnfinishedChange unfinished)
            {
                return new PreparedApp(app, _settingsStore);
            }

            switch (unfinished)
            {
                case UnfinishedChange.PutBack putBack:
                    string versions = string.Join(", ", putBack.Versions.Select(version => Describe(version, app)));
                    // Neither a service the change adds nor one it removed need be there to see.
                    ReachableServiceManager reachable = putBack.RemovedService != null
                        ? await _serviceManager.EnsureReachableAsync(cancellationToken)
                        : await _serviceDeployer.ReachServiceManagerAsync(app.Name, putBack.NewManifest, cancellationToken);
                    await EndAsync(app, "switching versions", $"putting back {versions}", () => _restorer.RestoreVersionsAsync(app, putBack, reachable));
                    break;
                case UnfinishedChange.CarryOut carryOut:
                    (string unfinishedWork, string ending) = carryOut.Restart ? ("restarting the service", "restarting it") : ("applying its settings", "applying them");
                    ReachableServiceManager toCarryOut = await _serviceDeployer.ReachServiceManagerAsync(app.Name, cancellationToken: cancellationToken);
                    await EndAsync(app, unfinishedWork, ending, () => _restorer.WriteFilesAsync(app, toCarryOut));
                    break;
                default:
                    throw new InvalidOperationException($"{app.Name} has an unfinished change of an unknown kind.");
            }

            UnfinishedChange? left = null;
            if (unfinished.Restart && await _restorer.RestartAsync(app.Name) is string problem)
            {
                _outputWriter.WriteError($"Warning: restarting {app.Name} failed: {problem}");
                left = new UnfinishedChange.CarryOut { Restart = true };
            }

            return new PreparedApp(await _settingsStore.SaveEntryAsync(app, left, CancellationToken.None), _settingsStore);
        }

        private async Task EndAsync(InstalledApp app, string unfinishedWork, string ending, Func<Task> end)
        {
            _outputWriter.WriteLine($"{app.Name}: an earlier run did not finish {unfinishedWork}; {ending}.");
            try
            {
                await end();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"{ending} failed: {ex.Message.TrimEnd('.')}. Until that works {app.Name} cannot be changed; 'updaemon remove {app.Name}' still removes it.", ex);
            }
        }

        private static string Describe(VersionBefore before, InstalledApp app) =>
            before.Version == null ? $"no {before.NameIn(app)}" : $"{before.NameIn(app)} {before.Version}";
    }
}
