using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'unset' command: removes an app's setting values and applies the change.
    /// </summary>
    public class UnsetCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly SettingsChanger _settings;
        private readonly IOutputWriter _outputWriter;

        public UnsetCommand(IConfigManager configManager, SettingsChanger settings, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _settings = settings;
            _outputWriter = outputWriter;
        }

        public string Name => "unset";

        public string Description => "Remove an app's settings";

        public string Usage => "updaemon unset <app> KEY...";

        public bool ChangesState(string[] args) => true;

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

            InstalledApp? app = await AppSelection.FindAppAsync(this, appName, _configManager, _outputWriter, cancellationToken);
            if (app == null)
            {
                return 1;
            }

            SettingsChange settings = await _settings.BeginAsync(app, cancellationToken);
            bool removedAny = false;
            foreach (string key in positionals.Skip(1))
            {
                if (settings.Manifest.Settings.TryGetValue(key, out SettingSpec? spec) && spec.IsRequired)
                {
                    return _outputWriter.Fail($"{appName} requires {key}. Give it a new value with 'updaemon set {appName} {key}=<value>'.");
                }

                if (settings.Change.Values.Remove(settings.Manifest, key))
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

            bool staysRunning = await _settings.ApplyAsync(settings, cancellationToken);
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
                  restarts its service as 'set' does, checking it keeps running.
                  Required settings cannot be removed, nor can the variables updaemon
                  sets itself.

                Examples:
                  updaemon unset byteshelf BYTESHELF_LOG_LEVEL
                """;
        }
    }
}
