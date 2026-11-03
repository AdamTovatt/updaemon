using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'remove' command: stops an app and deletes everything updaemon made for it.
    /// </summary>
    public class RemoveCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IOutputWriter _outputWriter;
        private readonly UpdaemonScope _scope;

        public RemoveCommand(IConfigManager configManager, IServiceDeployer serviceDeployer, IOutputWriter outputWriter, UpdaemonScope scope)
        {
            _configManager = configManager;
            _serviceDeployer = serviceDeployer;
            _outputWriter = outputWriter;
            _scope = scope;
        }

        public string Name => "remove";

        public string Description => "Remove an app";

        public string Usage => "updaemon remove <app> [--purge]";

        public bool ChangesState => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, "--purge");
            if (parser.GetUnknownFlags("--purge") is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            if (parser.Positionals is not [string appName])
            {
                return _outputWriter.FailUsage(this);
            }

            InstalledApp? app = await _configManager.GetAppAsync(appName, cancellationToken);
            if (app == null)
            {
                return _outputWriter.Fail($"'{appName}' is not installed.");
            }

            bool purge = parser.HasFlag("--purge");
            await _serviceDeployer.RemoveAsync(app, purge, cancellationToken);
            await _configManager.RemoveAppAsync(appName, cancellationToken);
            _outputWriter.WriteLine(purge
                ? $"Removed {appName} and its data."
                : $"Removed {appName}. Its data is kept in {_scope.GetDataDirectory(appName)}.");
            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                Remove Command

                Usage:
                  updaemon remove <app> [--purge]

                Description:
                  Stops the app's service and deletes its unit, its command, all its versions,
                  its settings and its secrets. Its data directory is kept, so a reinstall picks
                  up where it left off; --purge deletes that too.

                Examples:
                  updaemon remove byteshelf
                  updaemon remove byteshelf --purge
                """;
        }
    }
}
