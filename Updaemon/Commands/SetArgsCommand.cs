using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'set-args' command: overrides or restores the arguments a service runs with.
    /// </summary>
    public class SetArgsCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly SettingsChanger _settings;
        private readonly IOutputWriter _outputWriter;

        public SetArgsCommand(IConfigManager configManager, SettingsChanger settings, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _settings = settings;
            _outputWriter = outputWriter;
        }

        public string Name => "set-args";

        public string Description => "Set the arguments a service runs with";

        public string Usage => "updaemon set-args <app> [-- args...]";

        public bool ChangesState(string[] args) => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args);
            if (parser.GetUnknownFlags() is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            if (parser.Positionals is not [string appName])
            {
                return _outputWriter.FailUsage(this);
            }

            if (parser.Rest != null && !parser.Rest.All(SafeText.IsSafe))
            {
                return _outputWriter.Fail("An argument contains a control character, such as a line break.");
            }

            InstalledApp? app = await AppSelection.FindAppAsync(this, appName, _configManager, _outputWriter, cancellationToken);
            if (app == null)
            {
                return 1;
            }

            SettingsChange settings = await _settings.BeginAsync(app, cancellationToken);
            if (settings.Manifest.Service is not ServiceSpec service)
            {
                return _outputWriter.Fail($"{appName} is not a service.");
            }

            List<string>? given = parser.Rest?.ToList();
            settings.Change.After.Args = given;
            bool staysRunning = await _settings.ApplyAsync(settings, cancellationToken);

            IReadOnlyList<string> effective = given ?? service.Args;
            string shown = effective.Count == 0 ? "no arguments" : string.Join(' ', effective);
            _outputWriter.WriteLine(given == null
                ? $"{appName} runs with the arguments from its updaemon.json: {shown}."
                : $"{appName} runs with: {shown}.");
            return _outputWriter.FinishSettingsChange(appName, staysRunning);
        }

        public string GetDetailedHelp()
        {
            return """
                Set-Args Command

                Usage:
                  updaemon set-args <app> -- <args...>
                  updaemon set-args <app>

                Description:
                  Sets the arguments the app's service runs with, regenerates its unit and
                  restarts it as 'set' does, checking it keeps running.
                  Without '--', restores the arguments from the
                  app's updaemon.json. 'set-args <app> --' runs it with no arguments.

                Examples:
                  updaemon set-args byteshelf -- sync run --verbose
                  updaemon set-args byteshelf
                """;
        }
    }
}
