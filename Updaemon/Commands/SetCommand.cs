using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'set' command: sets an app's setting values and applies them.
    /// </summary>
    public class SetCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly SettingsChanger _settings;
        private readonly IOutputWriter _outputWriter;

        public SetCommand(IConfigManager configManager, SettingsChanger settings, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _settings = settings;
            _outputWriter = outputWriter;
        }

        public string Name => "set";

        public string Description => "Set an app's settings";

        public string Usage => "updaemon set <app> KEY=VALUE... [--secret]";

        public bool ChangesState(string[] args) => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, "--secret");
            if (parser.GetUnknownFlags("--secret") is [string unknown, ..])
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
            foreach (string assignment in positionals.Skip(1))
            {
                if (!SettingAssignment.TryParse(assignment, out string key, out string value, out string? error))
                {
                    return _outputWriter.Fail(error);
                }

                settings.Change.Values.Set(settings.Manifest, key, value, parser.HasFlag("--secret"));
            }

            bool staysRunning = await _settings.ApplyAsync(settings, cancellationToken);
            _outputWriter.WriteLine($"Updated the settings of {appName}.");
            return _outputWriter.FinishSettingsChange(appName, staysRunning);
        }

        public string GetDetailedHelp()
        {
            return """
                Set Command

                Usage:
                  updaemon set <app> KEY=VALUE... [--secret]

                Description:
                  Sets environment variables for an app, regenerates its service unit and
                  command, and restarts its service, running or not, so a crash-looping
                  one gets the change too: on Linux unless its unit was disabled, on
                  macOS always. A restarted service
                  must keep running for its health-check window, as after an update; if it
                  does not, set fails and the change stays. Settings the app's
                  updaemon.json marks secret are stored in a file only the user the app
                  runs as can read.
                  Use --secret to store another key that way; it stays secret when changed
                  later. To make it plain again, unset it, then set it.
                  Variables updaemon sets itself cannot be set: UPDAEMON_DATA_DIR and the
                  one that tells an app where its extensions are.

                Examples:
                  updaemon set byteshelf BYTESHELF_URL=https://shelf.example.com
                  updaemon set my-api EXTRA_TOKEN=abc123 --secret
                """;
        }
    }
}
