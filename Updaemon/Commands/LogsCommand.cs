using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'logs' command: shows an app service's log.
    /// </summary>
    public class LogsCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IServiceManager _serviceManager;
        private readonly IOutputWriter _outputWriter;

        public LogsCommand(IConfigManager configManager, IServiceDeployer serviceDeployer, IServiceManager serviceManager, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _serviceDeployer = serviceDeployer;
            _serviceManager = serviceManager;
            _outputWriter = outputWriter;
        }

        public string Name => "logs";

        public string Description => "Show an app's service log";

        public string Usage => "updaemon logs <app> [-f]";

        public bool ChangesState(string[] args) => false;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, "-f");
            bool follow = parser.HasFlag("-f");
            if (parser.GetUnknownFlags("-f") is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            if (parser.Positionals is not [string appName])
            {
                return _outputWriter.FailUsage(this);
            }

            if (await _configManager.GetAppAsync(appName, cancellationToken) == null)
            {
                return _outputWriter.Fail($"'{appName}' is not installed.");
            }

            AppManifest manifest = await _serviceDeployer.GetCurrentManifestAsync(appName, cancellationToken);
            if (!manifest.IsService)
            {
                return _outputWriter.Fail($"{appName} is not a service; its command writes to the terminal that runs it.");
            }

            return await _serviceManager.ShowLogsAsync(appName, follow, cancellationToken);
        }

        public string GetDetailedHelp()
        {
            return """
                Logs Command

                Usage:
                  updaemon logs <app> [-f]

                Description:
                  Shows the log of the app's service: its journal on Linux, its log files on
                  macOS. With -f, keeps showing new lines until Ctrl+C.

                Examples:
                  updaemon logs byteshelf
                  updaemon logs byteshelf -f
                """;
        }
    }
}
