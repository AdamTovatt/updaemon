using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'unset' command: removes an app's setting values and applies the change.
    /// </summary>
    public class UnsetCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly AppSettingsStore _settingsStore;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IOutputWriter _outputWriter;

        public UnsetCommand(IConfigManager configManager, AppSettingsStore settingsStore, IServiceDeployer serviceDeployer, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _settingsStore = settingsStore;
            _serviceDeployer = serviceDeployer;
            _outputWriter = outputWriter;
        }

        public string Name => "unset";

        public string Description => "Remove an app's settings";

        public string Usage => "updaemon unset <app> KEY...";

        public bool ChangesState => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args);
            if (parser.GetUnknownFlags() is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            if (parser.Positionals is not [string appName, _, ..] positionals)
            {
                return _outputWriter.FailUsage(this);
            }

            InstalledApp? app = await _configManager.GetAppAsync(appName, cancellationToken);
            if (app == null)
            {
                return _outputWriter.Fail($"'{appName}' is not installed.");
            }

            ReachableServiceManager reachable = await _serviceDeployer.ReachServiceManagerAsync(appName, cancellationToken: cancellationToken);
            AppManifest manifest = await _serviceDeployer.GetCurrentManifestAsync(appName, cancellationToken);
            AppValues values = await _settingsStore.LoadValuesAsync(app, cancellationToken);
            bool removedAny = false;
            foreach (string key in positionals.Skip(1))
            {
                if (manifest.Settings.TryGetValue(key, out SettingSpec? spec) && spec.IsRequired)
                {
                    return _outputWriter.Fail($"{appName} requires {key}. Give it a new value with 'updaemon set {appName} {key}=<value>'.");
                }

                if (values.Remove(key))
                {
                    removedAny = true;
                }
                else
                {
                    _outputWriter.WriteLine($"{key} was not set.");
                }
            }

            if (!removedAny)
            {
                return 0;
            }

            await _settingsStore.SaveAsync(values, cancellationToken);
            bool staysRunning = await _serviceDeployer.ApplySettingsAsync(app, reachable, cancellationToken);
            _outputWriter.WriteLine($"Updated the settings of {appName}.");
            return _outputWriter.FinishSettingsChange(appName, staysRunning);
        }

        public string GetDetailedHelp()
        {
            return """
                Unset Command

                Usage:
                  updaemon unset <app> KEY...

                Description:
                  Removes settings from an app, so it falls back to the default in its
                  updaemon.json, if any. Regenerates its service unit and command and
                  restarts its service if it is running, checking it keeps running as 'set'
                  does. Required settings cannot be removed.

                Examples:
                  updaemon unset byteshelf BYTESHELF_LOG_LEVEL
                """;
        }
    }
}
