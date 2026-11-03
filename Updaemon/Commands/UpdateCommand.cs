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
        private readonly AppSettingsStore _settingsStore;
        private readonly ISecretsManager _secretsManager;
        private readonly IDistributionServiceClient _distributionClient;
        private readonly IOutputWriter _outputWriter;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly AppRollback _appRollback;
        private readonly PluginProvider _pluginProvider;
        private readonly GitHubPluginUpdater _gitHubPluginUpdater;
        private readonly SelfUpdater _selfUpdater;
        private readonly PluginCredentials _credentials;

        public UpdateCommand(
            IConfigManager configManager,
            AppSettingsStore settingsStore,
            ISecretsManager secretsManager,
            IDistributionServiceClient distributionClient,
            IOutputWriter outputWriter,
            IServiceDeployer serviceDeployer,
            AppRollback appRollback,
            PluginProvider pluginProvider,
            GitHubPluginUpdater gitHubPluginUpdater,
            SelfUpdater selfUpdater,
            PluginCredentials credentials)
        {
            _configManager = configManager;
            _settingsStore = settingsStore;
            _secretsManager = secretsManager;
            _distributionClient = distributionClient;
            _outputWriter = outputWriter;
            _serviceDeployer = serviceDeployer;
            _appRollback = appRollback;
            _pluginProvider = pluginProvider;
            _gitHubPluginUpdater = gitHubPluginUpdater;
            _selfUpdater = selfUpdater;
            _credentials = credentials;
        }

        public string Name => "update";

        public string Description => "Update all apps and updaemon itself, or one app";

        public string Usage => "updaemon update [app]";

        public bool ChangesState => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args);
            if (parser.GetUnknownFlags() is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            IReadOnlyList<InstalledApp>? apps = await AppSelection.SelectAsync(this, parser.Positionals, _configManager, _outputWriter, cancellationToken);
            if (apps == null)
            {
                return 1;
            }

            if (apps.Count == 0)
            {
                _outputWriter.WriteLine("No apps installed. Use 'updaemon install <app> --from <plugin>:<remote>' to install one.");
            }

            UpdaemonConfig config = await _configManager.LoadConfigAsync(cancellationToken);
            bool allSucceeded = true;

            foreach (IGrouping<string, InstalledApp> pluginGroup in apps.GroupBy(app => app.Plugin))
            {
                allSucceeded &= await UpdateAppsForPluginAsync(pluginGroup.Key, pluginGroup.ToList(), config.ReleaseRetentionCount, cancellationToken);
            }

            // Last, so a run finishes on one version of the code. Updating one app leaves updaemon and its plugins alone.
            if (parser.Positionals is [])
            {
                allSucceeded &= await _gitHubPluginUpdater.RunAsync(cancellationToken);
                allSucceeded &= await _selfUpdater.RunAsync(cancellationToken);
            }

            return allSucceeded ? 0 : 1;
        }

        private async Task<bool> UpdateAppsForPluginAsync(string pluginAlias, List<InstalledApp> apps, int retentionCount, CancellationToken cancellationToken)
        {
            string appNames = string.Join(", ", apps.Select(app => app.Name));
            InstalledPluginInfo pluginInfo;
            try
            {
                pluginInfo = await _pluginProvider.GetOrInstallAsync(pluginAlias, cancellationToken);
            }
            catch (PluginException ex)
            {
                _outputWriter.WriteError($"Error: {appNames}: {ex.Message}");
                return false;
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
                _outputWriter.WriteError($"Error: {appNames}: the plugin '{pluginAlias}' failed to start: {ex.Message}");
                await DisposeClientAsync(pluginAlias);
                return false;
            }

            try
            {
                bool allSucceeded = true;
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
                        _outputWriter.WriteError($"Error: {app.Name}: {ex.Message}");
                        allSucceeded = false;
                    }
                }

                return allSucceeded;
            }
            finally
            {
                await DisposeClientAsync(pluginAlias);
            }
        }

        private async Task UpdateAppAsync(InstalledApp app, int retentionCount, CancellationToken cancellationToken)
        {
            Version currentVersion = await _serviceDeployer.GetCurrentVersionAsync(app.Name, cancellationToken);
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

            AppValues values = await _settingsStore.LoadValuesAsync(app, cancellationToken);
            try
            {
                values.EnsureComplete(staged.Manifest, latestVersion, currentVersion, $"updaemon update {app.Name}");
            }
            catch
            {
                _serviceDeployer.Discard(staged);
                throw;
            }

            // A release can newly declare a setting secret; its value then moves out of the readable settings.
            values.ConformTo(staged.Manifest);
            if (staged.ManifestInDirectory)
            {
                // The release ships its own manifest; one from the install flags is no longer used.
                app.Manifest = null;
            }

            await _settingsStore.SaveAsync(values, cancellationToken);
            await _serviceDeployer.ActivateAsync(app, staged, cancellationToken);

            if (staged.Manifest.IsService)
            {
                if (app.AutoRestart)
                {
                    await RestartAndCheckAsync(app, latestVersion, currentVersion, cancellationToken);
                }
                else
                {
                    _outputWriter.WriteLine($"{app.Name}: restart skipped (restart=manual); the service runs {currentVersion} until it restarts.");
                }
            }

            _outputWriter.WriteLine($"Updated {app.Name} from {currentVersion} to {latestVersion}.");

            try
            {
                await _serviceDeployer.PruneOldVersionsAsync(app.Name, retentionCount, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _outputWriter.WriteError($"Warning: {app.Name}: failed to prune old versions: {ex.Message}");
            }

            if (app.ForgetSkipsOlderThan(latestVersion, _serviceDeployer.GetRetainedVersions(app.Name)))
            {
                await _configManager.SaveAppAsync(app, cancellationToken);
            }
        }

        /// <summary>
        /// Restarts the service on the new version and checks it stays up. If it does not, the previous
        /// version runs again and the new one is skipped; the update then fails with that.
        /// </summary>
        private async Task RestartAndCheckAsync(InstalledApp app, Version newVersion, Version previousVersion, CancellationToken cancellationToken)
        {
            if (await _serviceDeployer.RestartAsync(app.Name, cancellationToken))
            {
                return;
            }

            bool previousStaysRunning;
            try
            {
                await _appRollback.SwitchAsync(app, newVersion, previousVersion, cancellationToken);
                previousStaysRunning = await _serviceDeployer.RestartAsync(app.Name, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    ServiceDeployer.NotRunning(app.Name, newVersion, $", and going back to {previousVersion} failed: {ex.Message.TrimEnd('.')}"));
            }

            string consequence = previousStaysRunning
                ? $", so {previousVersion} runs again and {newVersion} is skipped"
                : $", so it is skipped; {previousVersion} is current again but did not stay running either";
            throw new InvalidOperationException(ServiceDeployer.NotRunning(app.Name, newVersion, consequence));
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

                Examples:
                  updaemon update
                  updaemon update byteshelf
                """;
        }
    }
}
