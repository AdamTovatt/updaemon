using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'list' command to list all registered applications.
    /// </summary>
    public class ListCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IVersionExtractor _versionExtractor;

        public ListCommand(IConfigManager configManager, IOutputWriter outputWriter, IServiceDeployer serviceDeployer, IVersionExtractor versionExtractor)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
            _serviceDeployer = serviceDeployer;
            _versionExtractor = versionExtractor;
        }

        public string Name => "list";

        public string Description => "List all registered applications";

        public string Usage => "updaemon list";

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RegisteredService> services = await _configManager.GetAllServicesAsync(cancellationToken);

            if (services.Count == 0)
            {
                _outputWriter.WriteLine("No applications registered.");
                _outputWriter.WriteLine("Use 'updaemon new <app-name> --from <plugin-alias>' to register one.");
                return 0;
            }

            _outputWriter.WriteLine($"Registered applications ({services.Count}):");
            _outputWriter.WriteLine("");

            foreach (RegisteredService service in services)
            {
                string? currentTarget = await _serviceDeployer.ReadCurrentTargetAsync(service.LocalName, cancellationToken);
                string versionDisplay;

                if (currentTarget == null)
                {
                    versionDisplay = "not initialized";
                }
                else
                {
                    Version? version = _versionExtractor.ExtractVersionFromPath(currentTarget);
                    versionDisplay = version?.ToString() ?? "unknown";
                }

                _outputWriter.WriteLine($"  Name: {service.LocalName}");
                _outputWriter.WriteLine($"  Type: {service.ServiceType.ToLabel()}");
                if (service.ServiceType == ServiceType.Service)
                {
                    _outputWriter.WriteLine($"  Restart: {(service.AutoRestart ? "auto" : "manual")}");
                }
                _outputWriter.WriteLine($"  Version: {versionDisplay}");
                _outputWriter.WriteLine($"  Distribution plugin: {service.DistributionPluginAlias}");
                _outputWriter.WriteLine($"  Remote name: {service.RemoteName}");
                _outputWriter.WriteLine("");
            }

            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                List Command

                Usage:
                  updaemon list

                Description:
                  Lists all registered applications (services and CLI tools) with their
                  current version, initialization status, type, distribution plugin,
                  and remote name.

                Examples:
                  updaemon list
                """;
        }
    }
}
