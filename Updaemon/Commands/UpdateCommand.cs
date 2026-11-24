using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'update' command: updates every installed app with its extensions, or one, to its latest release.
    /// </summary>
    public class UpdateCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly AppPreparer _preparer;
        private readonly AppUpdater _appUpdater;
        private readonly GitHubPluginUpdater _gitHubPluginUpdater;
        private readonly SelfUpdater _selfUpdater;
        private readonly UpdateRunLog _runLog;
        private readonly IOutputWriter _outputWriter;
        private readonly TimeProvider _time;

        public UpdateCommand(
            IConfigManager configManager,
            AppPreparer preparer,
            AppUpdater appUpdater,
            GitHubPluginUpdater gitHubPluginUpdater,
            SelfUpdater selfUpdater,
            UpdateRunLog runLog,
            IOutputWriter outputWriter,
            TimeProvider time)
        {
            _configManager = configManager;
            _preparer = preparer;
            _appUpdater = appUpdater;
            _gitHubPluginUpdater = gitHubPluginUpdater;
            _selfUpdater = selfUpdater;
            _runLog = runLog;
            _outputWriter = outputWriter;
            _time = time;
        }

        public string Name => "update";

        public string Description => "Update all apps and updaemon itself, or one app or extension";

        public string Usage => "updaemon update [app[/extension]]";

        public bool ChangesState(string[] args) => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, TimerRun.Option);
            if (parser.GetUnknownFlags(TimerRun.Option) is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            IReadOnlyList<string> names;
            switch (parser.Positionals)
            {
                case []:
                    names = (await _configManager.GetAllAppsAsync(cancellationToken)).Select(app => app.Name).ToList();
                    break;
                case [string name]:
                    names = new[] { name };
                    break;
                default:
                    return _outputWriter.FailUsage(this);
            }

            if (names.Count == 0)
            {
                _outputWriter.WriteLine("No apps installed. Use 'updaemon install <app> --from <plugin>:<remote>' to install one.");
            }

            UpdaemonConfig config = await _configManager.LoadConfigAsync(cancellationToken);
            UpdateFailures failures = new UpdateFailures(_outputWriter);
            bool fullRun = parser.Positionals is [];
            IReadOnlyList<string> upToDate = Array.Empty<string>();

            try
            {
                IReadOnlyList<PreparedPackage>? selected = await PrepareAsync(names, failures, cancellationToken);
                if (selected == null)
                {
                    return 1;
                }

                upToDate = await _appUpdater.RunAsync(selected, config.ReleaseRetentionCount, failures, cancellationToken);

                // Last, so a run finishes on one version of the code. Updating one app leaves updaemon and its plugins alone.
                if (fullRun)
                {
                    await _gitHubPluginUpdater.RunAsync(failures, cancellationToken);
                    await _selfUpdater.RunAsync(failures, cancellationToken);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // What no step above reports itself, such as a timeout, still fails the run on its record.
                failures.Report("updaemon", ex.Message);
            }

            await RecordAsync(fullRun, parser.HasFlag(TimerRun.Option) ? UpdateStarter.Timer : UpdateStarter.Person, upToDate, failures, cancellationToken);
            return failures.All.Count == 0 ? 0 : 1;
        }

        /// <summary>All of them that can be changed; one that cannot is reported. Null when one is not installed, which is too.</summary>
        private async Task<IReadOnlyList<PreparedPackage>?> PrepareAsync(IReadOnlyList<string> names, UpdateFailures failures, CancellationToken cancellationToken)
        {
            List<PreparedPackage> packages = new List<PreparedPackage>();
            foreach (string name in names)
            {
                try
                {
                    PreparedPackage? package = await AppSelection.PrepareAsync(name, _configManager, _preparer, _outputWriter, cancellationToken);
                    if (package == null)
                    {
                        return null;
                    }

                    packages.Add(package);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Report(name, ex.Message);
                }
            }

            return packages;
        }

        /// <summary>
        /// Keeps a full run for 'status'. A run of one app or extension leaves the record alone, but clears the
        /// failures of what is now up to date: they are fixed. A run that updated what it could is not failed
        /// for want of its record.
        /// </summary>
        private async Task RecordAsync(bool fullRun, UpdateStarter startedBy, IReadOnlyList<string> upToDate, UpdateFailures failures, CancellationToken cancellationToken)
        {
            try
            {
                if (fullRun)
                {
                    await _runLog.SaveAsync(new UpdateRun(_time.GetUtcNow(), startedBy, failures.All), cancellationToken);
                }
                else
                {
                    await _runLog.ForgetFailuresAsync(upToDate, cancellationToken);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _outputWriter.WriteError($"Warning: this run could not be recorded for 'updaemon status': {ex.Message}");
            }
        }

        public string GetDetailedHelp()
        {
            return $"""
                Update Command

                Usage:
                  updaemon update [app[/extension]]

                Description:
                  Updates every installed app, or only the named one, to its latest release.
                  Each update reads the new release's updaemon.json and regenerates the app's
                  command and service unit from it. If the new release needs a setting the
                  app has no value for, or no longer takes extensions while any is installed,
                  it is not activated: the current version keeps running, its extensions are
                  still updated and the message says what to run.

                  An app's extensions are updated with it: everything new is downloaded
                  first, the app is asked whether it would load each extension if it can
                  tell, and what it accepts is switched with one restart. One it refuses
                  stays at its version and is reported by every update until that is fixed
                  or the extension is removed. If the app does not keep running, all that
                  was switched goes back and is skipped. <app>/<extension> updates only that
                  extension; a service disabled on Linux is not restarted for extensions
                  alone.

                  When a plugin needs a secret, such as a githubToken for a private
                  repository, it is asked for at a terminal without echo and saved; under
                  the timer, the update fails naming the 'updaemon secret-set' command.

                  Services are restarted after an update unless set to manual with
                  'updaemon set-restart <app> manual'. A restarted service must keep running
                  for its health-check window ({ServiceSpec.DefaultHealthCheckSeconds} seconds unless its updaemon.json sets
                  service.healthCheckSeconds); if it does not, the previous version runs again and
                  the new one is skipped. A skipped version, or one rolled back from, is not
                  offered again; update waits for a newer release.

                  If the switch itself fails or is cancelled, everything goes back, nothing is
                  skipped, and the next run tries again. If the run is killed, the next command
                  that changes the app puts back what was there before.

                  Old version directories are pruned after each update; releaseRetentionCount
                  in config.json sets how many to keep (default 5).

                  Without an app named, and unless 'updaemon self-update off' turned this off,
                  update ends by installing newer releases of plugins installed with
                  'dist-install github:<owner>/<repo>', then updating updaemon itself to its
                  latest release.

                  'updaemon status' shows the last run without an app named: when it
                  finished, what failed and why, and whether the update timer started it
                  (the timer runs 'updaemon {string.Join(' ', TimerRun.Arguments)}'). Updating one app does not
                  replace that record, but clears from it the failures of what it updated.
                  So does removing an app or an extension.

                Examples:
                  updaemon update
                  updaemon update byteshelf
                  updaemon update byteshelf/images
                """;
        }
    }
}
