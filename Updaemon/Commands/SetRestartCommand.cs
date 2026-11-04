using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'set-restart' command to control whether a service is restarted after an update.
    /// </summary>
    public class SetRestartCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;
        private readonly IServiceDeployer _serviceDeployer;

        public SetRestartCommand(IConfigManager configManager, IOutputWriter outputWriter, IServiceDeployer serviceDeployer)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
            _serviceDeployer = serviceDeployer;
        }

        public string Name => "set-restart";

        public string Description => "Set restart behavior after update (auto/manual)";

        public string Usage => "updaemon set-restart <app> auto|manual";

        public bool ChangesState(string[] args) => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args);
            if (parser.GetUnknownFlags() is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            if (parser.Positionals is not [string appName, string mode])
            {
                return _outputWriter.FailUsage(this);
            }

            bool autoRestart;
            switch (mode.ToLowerInvariant())
            {
                case "auto":
                    autoRestart = true;
                    break;
                case "manual":
                    autoRestart = false;
                    break;
                default:
                    return _outputWriter.Fail($"Invalid restart mode '{mode}'. Expected 'auto' or 'manual'.");
            }

            InstalledApp? app = await _configManager.GetAppAsync(appName, cancellationToken);
            if (app == null)
            {
                return _outputWriter.Fail($"'{appName}' is not installed.");
            }

            app.AutoRestart = autoRestart;
            await _configManager.SaveAppAsync(app, cancellationToken);
            _outputWriter.WriteLine($"Restart behavior of '{appName}' set to '{mode.ToLowerInvariant()}'");

            // Auto-restart only affects services; commands are never restarted.
            AppManifest manifest = await _serviceDeployer.GetCurrentManifestAsync(appName, cancellationToken);
            if (!manifest.IsService)
            {
                _outputWriter.WriteLine("Note: this setting has no effect for an app without a service.");
            }

            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                Set-Restart Command

                Usage:
                  updaemon set-restart <app> auto|manual

                Description:
                  Controls whether a service is restarted after 'update' deploys a new version.

                    auto    (default) Restart the service after deploying a new version.
                    manual  Deploy the new version (download, repoint symlink, prune) but leave
                            the running process untouched, so the application can restart on
                            demand. The service keeps running the previous version until it
                            restarts.

                  Only meaningful for apps with a service; commands are never restarted.

                Examples:
                  updaemon set-restart my-api manual
                  updaemon set-restart my-api auto
                """;
        }
    }
}
