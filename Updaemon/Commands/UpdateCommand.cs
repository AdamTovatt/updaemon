using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'update' command: updates every installed app, or one, to its latest release.
    /// </summary>
    public class UpdateCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly ISecretsManager _secretsManager;
        private readonly IDistributionServiceClient _distributionClient;
        private readonly IOutputWriter _outputWriter;
        private readonly VersionStore _versions;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly AppSwitcher _switcher;
        private readonly PluginProvider _pluginProvider;
        private readonly GitHubPluginUpdater _gitHubPluginUpdater;
        private readonly SelfUpdater _selfUpdater;
        private readonly PluginCredentials _credentials;
        private readonly UpdateRunLog _runLog;
        private readonly TimeProvider _time;

        public UpdateCommand(
            IConfigManager configManager,
            ISecretsManager secretsManager,
            IDistributionServiceClient distributionClient,
            IOutputWriter outputWriter,
            VersionStore versions,
            IServiceDeployer serviceDeployer,
            AppSwitcher switcher,
            PluginProvider pluginProvider,
            GitHubPluginUpdater gitHubPluginUpdater,
            SelfUpdater selfUpdater,
            PluginCredentials credentials,
            UpdateRunLog runLog,
            TimeProvider time)
        {
            _configManager = configManager;
            _secretsManager = secretsManager;
            _distributionClient = distributionClient;
            _outputWriter = outputWriter;
            _versions = versions;
            _serviceDeployer = serviceDeployer;
            _switcher = switcher;
            _pluginProvider = pluginProvider;
            _gitHubPluginUpdater = gitHubPluginUpdater;
            _selfUpdater = selfUpdater;
            _credentials = credentials;
            _runLog = runLog;
            _time = time;
        }

        public string Name => "update";

        public string Description => "Update all apps and updaemon itself, or one app";

        public string Usage => "updaemon update [app]";

        public bool ChangesState(string[] args) => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, TimerRun.Option);
            if (parser.GetUnknownFlags(TimerRun.Option) is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            IReadOnlyList<SelectedPackage>? selected = await AppSelection.SelectAsync(this, parser.Positionals, _configManager, _outputWriter, cancellationToken);
            if (selected == null)
            {
                return 1;
            }

            if (selected.FirstOrDefault(package => package.Extension != null) is SelectedPackage extension)
            {
                return _outputWriter.Fail($"'{extension.Name}' names an extension, which 'update' does not work on. For its host, use 'updaemon update {extension.App.Name}'.");
            }

            IReadOnlyList<InstalledApp> apps = selected.Select(package => package.App).ToList();

            if (apps.Count == 0)
            {
                _outputWriter.WriteLine("No apps installed. Use 'updaemon install <app> --from <plugin>:<remote>' to install one.");
            }

            UpdaemonConfig config = await _configManager.LoadConfigAsync(cancellationToken);
            UpdateFailures failures = new UpdateFailures(_outputWriter);
            bool fullRun = parser.Positionals is [];

            try
            {
                foreach (IGrouping<string, InstalledApp> pluginGroup in apps.GroupBy(app => app.Plugin))
                {
                    await UpdateAppsForPluginAsync(pluginGroup.Key, pluginGroup.ToList(), config.ReleaseRetentionCount, failures, cancellationToken);
                }

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

            await RecordAsync(fullRun, parser.HasFlag(TimerRun.Option) ?UpdateStarter.Timer : UpdateStarter.Person, apps, failures, cancellationToken);
            return failures.All.Count == 0 ? 0 : 1;
        }

        /// <summary>
        /// Keeps a full run for 'status'. A run of named apps leaves the record alone, but clears the
        /// failures of those it updated: they are fixed. A run that updated what it could is not failed
        /// for want of its record.
        /// </summary>
        private async Task RecordAsync(bool fullRun, UpdateStarter startedBy, IReadOnlyList<InstalledApp> apps, UpdateFailures failures, CancellationToken cancellationToken)
        {
            try
            {
                if (fullRun)
                {
                    await _runLog.SaveAsync(new UpdateRun(_time.GetUtcNow(), startedBy, failures.All), cancellationToken);
                }
                else if (failures.All.Count == 0)
                {
                    await _runLog.ForgetFailuresAsync(apps.Select(app => app.Name), cancellationToken);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _outputWriter.WriteError($"Warning: this run could not be recorded for 'updaemon status': {ex.Message}");
            }
        }

        private async Task UpdateAppsForPluginAsync(string pluginAlias, List<InstalledApp> apps, int retentionCount, UpdateFailures failures, CancellationToken cancellationToken)
        {
            string[] appNames = apps.Select(app => app.Name).ToArray();
            InstalledPluginInfo pluginInfo;
            try
            {
                pluginInfo = await _pluginProvider.GetOrInstallAsync(pluginAlias, cancellationToken);
            }
            catch (PluginException ex)
            {
                failures.Report(appNames, ex.Message);
                return;
            }

            // The running plugin gets the secrets as stored; again after one is saved at the prompt, so
            // the apps after that one use it too.
            async Task InitializeAsync() =>
                await _distributionClient.InitializeAsync(await _secretsManager.GetPluginSecretsFormattedAsync(pluginAlias, cancellationToken), cancellationToken);

            try
            {
                await _distributionClient.ConnectAsync(pluginInfo.Path, cancellationToken);
                await InitializeAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Report(appNames, $"the plugin '{pluginAlias}' failed to start: {ex.Message}");
                await DisposeClientAsync(pluginAlias);
                return;
            }

            try
            {
                foreach (InstalledApp app in apps)
                {
                    try
                    {
                        await _credentials.RunAsync(
                            pluginAlias,
                            async () =>
                            {
                                await UpdateAppAsync(app, retentionCount, cancellationToken);
                                return true;
                            },
                            handOverSavedSecret: InitializeAsync,
                            cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Report(app.Name, ex.Message);
                    }
                }
            }
            finally
            {
                await DisposeClientAsync(pluginAlias);
            }
        }

        private async Task UpdateAppAsync(InstalledApp app, int retentionCount, CancellationToken cancellationToken)
        {
            PreparedApp prepared = await _switcher.PrepareAsync(app, cancellationToken);
            Version currentVersion = await _versions.GetCurrentAsync(app.PackageName, cancellationToken);
            Version? latestVersion = await _distributionClient.GetLatestVersionAsync(app.Remote, cancellationToken);
            if (latestVersion == null)
            {
                _outputWriter.WriteLine($"{app.Name}: no released version found for {app.Source}.");
                return;
            }

            if (latestVersion <= currentVersion)
            {
                _outputWriter.WriteLine($"{app.Name} is up to date ({currentVersion}).");
                return;
            }

            if (app.IsSkipped(latestVersion))
            {
                _outputWriter.WriteLine($"{app.Name}: {latestVersion} is skipped (it failed its health check or was rolled back from); {currentVersion} stays until a newer version is released.");
                return;
            }

            DeployableVersion staged = await _serviceDeployer.StageAsync(app, latestVersion, _distributionClient, cancellationToken);

            try
            {
                await _switcher.UpdateAsync(AppSwitch.ToVersion(prepared, staged), cancellationToken);
            }
            catch (Exception ex) when (ex is not NotRunningException)
            {
                // A full disk or an unwritable directory included: the current version stays, and
                // nothing of the new one is left.
                await _versions.DiscardAsync(app.PackageName, latestVersion);
                throw;
            }

            _outputWriter.WriteLine($"Updated {app.Name} from {currentVersion} to {latestVersion}.");

            try
            {
                await _versions.PruneAsync(app.PackageName, retentionCount, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _outputWriter.WriteError($"Warning: {app.Name}: failed to prune old versions: {ex.Message}");
            }

            if (app.ForgetSkipsOlderThan(latestVersion, _versions.GetRetained(app.PackageName)))
            {
                await _configManager.SaveAppAsync(app, cancellationToken);
            }
        }

        private async Task DisposeClientAsync(string pluginAlias)
        {
            try
            {
                await _distributionClient.DisposeAsync();
            }
            catch (Exception ex)
            {
                _outputWriter.WriteError($"Warning: failed to stop the plugin '{pluginAlias}': {ex.Message}");
            }
        }

        public string GetDetailedHelp()
        {
            return $"""
                Update Command

                Usage:
                  updaemon update [app]

                Description:
                  Updates every installed app, or only the named one, to its latest release.
                  Each update reads the new release's updaemon.json and regenerates the app's
                  command and service unit from it. If the new release needs a setting the
                  app has no value for, it is not activated: the current version keeps running
                  and the message says which 'updaemon set' command to run.

                  When a plugin needs a secret, such as a githubToken for a private
                  repository, it is asked for at a terminal without echo and saved; under
                  the timer, the update fails naming the 'updaemon secret-set' command.

                  Services are restarted after an update unless set to manual with
                  'updaemon set-restart <app> manual'. A restarted service must keep running
                  for its health-check window ({ServiceSpec.DefaultHealthCheckSeconds} seconds unless its updaemon.json sets
                  service.healthCheckSeconds); if it does not, the previous version runs again and
                  the new one is skipped. A skipped version, or one rolled back from, is not
                  offered again; update waits for a newer release.

                  Old version directories are pruned after each update; releaseRetentionCount
                  in config.json sets how many to keep (default 5).

                  Without an app named, and unless 'updaemon self-update off' turned this off,
                  update ends by installing newer releases of plugins installed with
                  'dist-install github:<owner>/<repo>', then updating updaemon itself to its
                  latest release.

                  'updaemon status' shows the last run without an app named: when it
                  finished, what failed and why, and whether the update timer started it
                  (the timer runs 'updaemon {string.Join(' ', TimerRun.Arguments)}'). Updating one app does not
                  replace that record; when it succeeds, it clears that app's failure from it.
                  So does removing an app.

                Examples:
                  updaemon update
                  updaemon update byteshelf
                """;
        }
    }
}
