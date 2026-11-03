using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'set-args' command: overrides or restores the arguments a service runs with.
    /// </summary>
    public class SetArgsCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IOutputWriter _outputWriter;

        public SetArgsCommand(IConfigManager configManager, IServiceDeployer serviceDeployer, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _serviceDeployer = serviceDeployer;
            _outputWriter = outputWriter;
        }

        public string Name => "set-args";

        public string Description => "Set the arguments a service runs with";

        public string Usage => "updaemon set-args <app> [-- args...]";

        public bool ChangesState => true;

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

            InstalledApp? app = await _configManager.GetAppAsync(appName, cancellationToken);
            if (app == null)
            {
                return _outputWriter.Fail($"'{appName}' is not installed.");
            }

            AppManifest manifest = await _serviceDeployer.GetCurrentManifestAsync(appName, cancellationToken);
            if (manifest.Service == null)
            {
                return _outputWriter.Fail($"{appName} is not a service.");
            }

            app.Args = parser.Rest?.ToList();
            await _configManager.SaveAppAsync(app, cancellationToken);
            await _serviceDeployer.ApplySettingsAsync(app, cancellationToken);

            IReadOnlyList<string> effective = app.Args ?? manifest.Service.Args;
            string shown = effective.Count == 0 ? "no arguments" : string.Join(' ', effective);
            _outputWriter.WriteLine(app.Args == null
                ? $"{appName} runs with the arguments from its updaemon.json: {shown}."
                : $"{appName} runs with: {shown}.");
            return 0;
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
                  restarts it if it is running. Without '--', restores the arguments from the
                  app's updaemon.json. 'set-args <app> --' runs it with no arguments.

                Examples:
                  updaemon set-args byteshelf -- sync run --verbose
                  updaemon set-args byteshelf
                """;
        }
    }
}
