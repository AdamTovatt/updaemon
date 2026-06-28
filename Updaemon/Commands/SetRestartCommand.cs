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

        public SetRestartCommand(IConfigManager configManager, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
        }

        public string Name => "set-restart";

        public string Description => "Set restart behavior after update (auto/manual)";

        public string Usage => "updaemon set-restart <app-name> auto|manual";

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, _outputWriter);

            if (!parser.ValidateMinimumArgs(2, Usage, out int errorCode))
            {
                return errorCode;
            }

            string localName = parser.GetPositional(0)!;
            string mode = parser.GetPositional(1)!;

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
                    _outputWriter.WriteError($"Error: Invalid restart mode '{mode}'. Expected 'auto' or 'manual'.");
                    _outputWriter.WriteLine($"Usage: {Usage}");
                    return 1;
            }

            _outputWriter.WriteLine($"Setting restart behavior for '{localName}' to '{mode.ToLowerInvariant()}'");

            try
            {
                await _configManager.SetAutoRestartAsync(localName, autoRestart, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _outputWriter.WriteError($"Error: {ex.Message}");
                return 1;
            }

            _outputWriter.WriteLine("Restart behavior updated successfully");

            // Auto-restart only affects services; CLI tools are never restarted.
            RegisteredService? service = await _configManager.GetServiceAsync(localName, cancellationToken);
            if (service != null && service.ServiceType == ServiceType.Cli)
            {
                _outputWriter.WriteLine("Note: this setting has no effect for CLI tools, which are never restarted.");
            }

            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                Set-Restart Command

                Usage:
                  updaemon set-restart <app-name> auto|manual

                Description:
                  Controls whether a service is restarted after 'update' deploys a new version.

                    auto    (default) Restart the service after deploying a new version.
                    manual  Deploy the new version (download, repoint symlink, prune) but leave
                            the running process untouched, so the application can restart on
                            demand. The service keeps running the previous version until it
                            restarts.

                  Only meaningful for services; CLI tools are never restarted.

                Examples:
                  updaemon set-restart my-api manual
                  updaemon set-restart my-api auto
                """;
        }
    }
}
