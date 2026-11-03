using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

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

        public UpdateCommand(
            IConfigManager configManager,
            AppSettingsStore settingsStore,
            ISecretsManager secretsManager,
            IDistributionServiceClient distributionClient,
            IOutputWriter outputWriter,
            IServiceDeployer serviceDeployer)
        {
            _configManager = configManager;
            _settingsStore = settingsStore;
            _secretsManager = secretsManager;
            _distributionClient = distributionClient;
            _outputWriter = outputWriter;
            _serviceDeployer = serviceDeployer;
        }

        public string Name => "update";

        public string Description => "Update all apps, or one";

        public string Usage => "updaemon update [app]";

        public bool ChangesState => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args);
            if (parser.GetUnknownFlags() is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            IReadOnlyList<InstalledApp> apps;
            switch (parser.Positionals)
            {
                case []:
                    apps = await _configManager.GetAllAppsAsync(cancellationToken);
                    break;
                case [string appName]:
                    InstalledApp? app = await _configManager.GetAppAsync(appName, cancellationToken);
                    if (app == null)
                    {
                        return _outputWriter.Fail($"'{appName}' is not installed.");
                    }

                    apps = new[] { app };
                    break;
                default:
                    return _outputWriter.FailUsage(this);
            }

            if (apps.Count == 0)
            {
                _outputWriter.WriteLine("No apps installed. Use 'updaemon install <app> --from <plugin>:<remote>' to install one.");
                return 0;
            }

            UpdaemonConfig config = await _configManager.LoadConfigAsync(cancellationToken);
            bool allSucceeded = true;

            foreach (IGrouping<string, InstalledApp> pluginGroup in apps.GroupBy(app => app.Plugin))
            {
                allSucceeded &= await UpdateAppsForPluginAsync(pluginGroup.Key, pluginGroup.ToList(), config.ReleaseRetentionCount, cancellationToken);
            }

            return allSucceeded ? 0 : 1;
        }

        private async Task<bool> UpdateAppsForPluginAsync(string pluginAlias, List<InstalledApp> apps, int retentionCount, CancellationToken cancellationToken)
        {
            InstalledPluginInfo? pluginInfo = await _configManager.GetPluginAsync(pluginAlias, cancellationToken);
            if (pluginInfo == null)
            {
                foreach (InstalledApp app in apps)
                {
                    _outputWriter.WriteError($"Error: {app.Name}: the plugin '{pluginAlias}' is not installed. Install it with 'updaemon dist-install {pluginAlias}'.");
                }

                return false;
            }

            try
            {
                await _distributionClient.ConnectAsync(pluginInfo.Path, cancellationToken);
                string? secrets = await _secretsManager.GetPluginSecretsFormattedAsync(pluginAlias, cancellationToken);
                await _distributionClient.InitializeAsync(secrets, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string appNames = string.Join(", ", apps.Select(app => app.Name));
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
                        await UpdateAppAsync(app, retentionCount, cancellationToken);
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

            StagedVersion staged = await _serviceDeployer.StageAsync(app, latestVersion, _distributionClient, cancellationToken);

            AppValues values = await _settingsStore.LoadValuesAsync(app, cancellationToken);
            IReadOnlyList<string> missing = values.MissingRequired(staged.Manifest);
            if (missing.Count > 0)
            {
                _serviceDeployer.Discard(staged);
                string assignments = string.Join(' ', missing.Select(key => $"{key}=<value>"));
                throw new InvalidOperationException(
                    $"{latestVersion} needs {string.Join(", ", missing)}; {currentVersion} keeps running. Run 'updaemon set {app.Name} {assignments}', then 'updaemon update {app.Name}'.");
            }

            // A release can newly declare a setting secret; its value then moves out of the readable settings.
            values.ConformTo(staged.Manifest);
            if (staged.ManifestFromRelease)
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
                    await _serviceDeployer.RestartAsync(app.Name, cancellationToken);
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
            return """
                Update Command

                Usage:
                  updaemon update [app]

                Description:
                  Updates every installed app, or only the named one, to its latest release.
                  Each update reads the new release's updaemon.json and regenerates the app's
                  command and service unit from it. If the new release needs a setting the
                  app has no value for, it is not activated: the current version keeps running
                  and the message says which 'updaemon set' command to run.

                  Services are restarted after an update unless set to manual with
                  'updaemon set-restart <app> manual'.

                  Old version directories are pruned after each update; releaseRetentionCount
                  in config.json sets how many to keep (default 5).

                Examples:
                  updaemon update
                  updaemon update byteshelf
                """;
        }
    }
}
