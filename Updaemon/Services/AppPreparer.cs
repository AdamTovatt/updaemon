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
        private readonly AppEntryStore _entryStore;
        private readonly IOutputWriter _outputWriter;

        public AppPreparer(
            SwitchRestorer restorer, IServiceDeployer serviceDeployer, IServiceManager serviceManager, AppEntryStore entryStore, IOutputWriter outputWriter)
        {
            _restorer = restorer;
            _serviceDeployer = serviceDeployer;
            _serviceManager = serviceManager;
            _entryStore = entryStore;
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
                return new PreparedApp(app, _entryStore);
            }

            switch (unfinished)
            {
                case UnfinishedChange.PutBack putBack:
                    // Neither a service the change adds nor one it removed need be there to see.
                    ReachableServiceManager reachable = putBack.App?.RemovedService != null
                        ? await _serviceManager.EnsureReachableAsync(cancellationToken)
                        : await _serviceDeployer.ReachServiceManagerAsync(app.Name, putBack.App?.NewManifest, cancellationToken);
                    await EndAsync(app, "switching versions", $"putting back {Describe(putBack, app)}", () => _restorer.RestoreVersionsAsync(app, putBack, reachable));
                    break;
                case UnfinishedChange.CarryOut carryOut:
                    (string unfinishedWork, string ending) = carryOut.Restart ? ("restarting the service", "restarting it") : ("applying its settings", "applying them");
                    ReachableServiceManager toCarryOut = await _serviceDeployer.ReachServiceManagerAsync(app.Name, cancellationToken: cancellationToken);
                    await EndAsync(app, unfinishedWork, ending, () => _restorer.WriteFilesAsync(app, toCarryOut));
                    break;
                default:
                    throw new InvalidOperationException($"{app.Name} has an unfinished change of an unknown kind.");
            }

            UnfinishedChange? left = unfinished.Restart && !await TryRestartAsync(app.Name) ? new UnfinishedChange.CarryOut { Restart = true } : null;
            return new PreparedApp(await _entryStore.SaveEntryAsync(app, left, CancellationToken.None), _entryStore);
        }

        /// <summary>
        /// Restarts the service, if there is one, without a health check. Not one that was both stopped and
        /// disabled: no change does that to it, so the user did. One that is only disabled may run what the
        /// change gave up.
        /// </summary>
        /// <returns>Whether no restart is owed any more.</returns>
        private async Task<bool> TryRestartAsync(string appName)
        {
            if (!_serviceDeployer.HasService(appName))
            {
                return true;
            }

            try
            {
                if (!await _serviceManager.IsEnabledAsync(appName, CancellationToken.None) && !(await _serviceManager.GetStateAsync(appName, CancellationToken.None)).IsActive)
                {
                    _outputWriter.WriteLine($"{appName}: not restarted, as its service is stopped and disabled.");
                }
                else
                {
                    await _serviceManager.RestartServiceAsync(appName, CancellationToken.None);
                }

                return true;
            }
            catch (Exception ex)
            {
                _outputWriter.WriteError($"Warning: restarting {appName} failed: {ex.Message}");
                return false;
            }
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

        private static string Describe(UnfinishedChange.PutBack putBack, InstalledApp app)
        {
            IEnumerable<string> extensions = putBack.Extensions.Select(extension => Describe(new ExtensionName(app.Name, extension.Id), extension.Version));
            return string.Join(", ", putBack.App == null ? extensions : extensions.Prepend(Describe(app.PackageName, putBack.App.Version)));
        }

        private static string Describe(PackageName name, Version? version) => version == null ? $"no {name}" : $"{name} {version}";
    }
}
