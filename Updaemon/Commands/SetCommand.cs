using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'set' command: sets an app's setting values and applies them.
    /// </summary>
    public class SetCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly AppSettingsStore _settingsStore;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IOutputWriter _outputWriter;

        public SetCommand(IConfigManager configManager, AppSettingsStore settingsStore, IServiceDeployer serviceDeployer, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _settingsStore = settingsStore;
            _serviceDeployer = serviceDeployer;
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

            InstalledApp? app = await _configManager.GetAppAsync(appName, cancellationToken);
            if (app == null)
            {
                return _outputWriter.Fail($"'{appName}' is not installed.");
            }

            ReachableServiceManager reachable = await _serviceDeployer.ReachServiceManagerAsync(appName, cancellationToken: cancellationToken);
            AppManifest manifest = await _serviceDeployer.GetCurrentManifestAsync(appName, cancellationToken);
            AppValues values = await _settingsStore.LoadValuesAsync(app, cancellationToken);
            foreach (string assignment in positionals.Skip(1))
            {
                if (!SettingAssignment.TryParse(assignment, out string key, out string value, out string? error))
                {
                    return _outputWriter.Fail(error);
                }

                values.Set(manifest, key, value, parser.HasFlag("--secret"));
            }

            await _settingsStore.SaveAsync(values, cancellationToken);
            bool staysRunning = await _serviceDeployer.ApplySettingsAsync(app, reachable, cancellationToken);
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

                Examples:
                  updaemon set byteshelf BYTESHELF_URL=https://shelf.example.com
                  updaemon set my-api EXTRA_TOKEN=abc123 --secret
                """;
        }
    }
}
