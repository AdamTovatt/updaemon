using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'update' command to update services.
    /// </summary>
    public class UpdateCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly ISecretsManager _secretsManager;
        private readonly IServiceManager _serviceManager;
        private readonly IDistributionServiceClient _distributionClient;
        private readonly IOutputWriter _outputWriter;
        private readonly IVersionExtractor _versionExtractor;
        private readonly IServiceDeployer _serviceDeployer;

        public UpdateCommand(
            IConfigManager configManager,
            ISecretsManager secretsManager,
            IServiceManager serviceManager,
            IDistributionServiceClient distributionClient,
            IOutputWriter outputWriter,
            IVersionExtractor versionExtractor,
            IServiceDeployer serviceDeployer)
        {
            _configManager = configManager;
            _secretsManager = secretsManager;
            _serviceManager = serviceManager;
            _distributionClient = distributionClient;
            _outputWriter = outputWriter;
            _versionExtractor = versionExtractor;
            _serviceDeployer = serviceDeployer;
        }

        public string Name => "update";

        public string Description => "Update all services or a specific service";

        public string Usage => "updaemon update [app-name]";

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            string? specificAppName = args.Length > 0 ? args[0] : null;

            // Get services to update
            IReadOnlyList<RegisteredService> services;
            if (specificAppName != null)
            {
                RegisteredService? service = await _configManager.GetServiceAsync(specificAppName, cancellationToken);
                if (service == null)
                {
                    _outputWriter.WriteError($"Error: Service '{specificAppName}' is not registered.");
                    return 1;
                }

                services = new[] { service };
            }
            else
            {
                services = await _configManager.GetAllServicesAsync(cancellationToken);
            }

            if (services.Count == 0)
            {
                _outputWriter.WriteLine("No services registered. Use 'updaemon new <app-name> --from <plugin>' to create a service.");
                return 0;
            }

            // Group services by plugin alias
            Dictionary<string, List<RegisteredService>> servicesByPlugin = new Dictionary<string, List<RegisteredService>>();
            foreach (RegisteredService service in services)
            {
                if (string.IsNullOrEmpty(service.DistributionPluginAlias))
                {
                    _outputWriter.WriteError($"Error: Service '{service.LocalName}' does not have a distribution plugin assigned.");
                    continue;
                }

                if (!servicesByPlugin.ContainsKey(service.DistributionPluginAlias))
                {
                    servicesByPlugin[service.DistributionPluginAlias] = new List<RegisteredService>();
                }

                servicesByPlugin[service.DistributionPluginAlias].Add(service);
            }

            if (servicesByPlugin.Count == 0)
            {
                _outputWriter.WriteError("Error: No valid services to update.");
                return 1;
            }

            // Update services grouped by plugin
            foreach (KeyValuePair<string, List<RegisteredService>> pluginGroup in servicesByPlugin)
            {
                string pluginAlias = pluginGroup.Key;
                List<RegisteredService> pluginServices = pluginGroup.Value;

                await UpdateServicesForPluginAsync(pluginAlias, pluginServices, cancellationToken);
            }

            return 0;
        }

        private async Task UpdateServicesForPluginAsync(string pluginAlias, List<RegisteredService> services, CancellationToken cancellationToken)
        {
            // Get plugin info
            InstalledPluginInfo? pluginInfo = await _configManager.GetPluginAsync(pluginAlias, cancellationToken);
            if (pluginInfo == null)
            {
                _outputWriter.WriteError($"Error: Plugin '{pluginAlias}' not found. Services using this plugin will be skipped.");
                return;
            }

            if (!File.Exists(pluginInfo.Path))
            {
                _outputWriter.WriteError($"Error: Plugin executable not found at '{pluginInfo.Path}'. Services using this plugin will be skipped.");
                return;
            }

            _outputWriter.WriteLine($"\n=== Updating services using plugin '{pluginAlias}' ===");

            // Connect to the distribution service
            await _distributionClient.ConnectAsync(pluginInfo.Path, cancellationToken);

            // Load plugin-specific secrets
            string? secrets = await _secretsManager.GetPluginSecretsFormattedAsync(pluginAlias, cancellationToken);
            await _distributionClient.InitializeAsync(secrets, cancellationToken);

            // Update each service for this plugin
            foreach (RegisteredService service in services)
            {
                await UpdateServiceAsync(service, cancellationToken);
            }

            // Disconnect from plugin
            await _distributionClient.DisposeAsync();
        }

        private async Task UpdateServiceAsync(RegisteredService service, CancellationToken cancellationToken)
        {
            _outputWriter.WriteLine($"\nUpdating service: {service.LocalName}");

            try
            {
                // Check if service is initialized
                string? currentTarget = await _serviceDeployer.ReadCurrentTargetAsync(service.LocalName, cancellationToken);
                if (currentTarget == null)
                {
                    _outputWriter.WriteLine($"Skipping '{service.LocalName}': not initialized. Run 'updaemon init {service.LocalName}' first.");
                    return;
                }

                // Get current version
                Version? currentVersion = _versionExtractor.ExtractVersionFromPath(currentTarget);
                if (currentVersion != null)
                {
                    _outputWriter.WriteLine($"Current version: {currentVersion}");
                }
                else
                {
                    _outputWriter.WriteLine("No version currently installed");
                }

                // Get latest version from distribution service
                Version? latestVersion = await _distributionClient.GetLatestVersionAsync(service.RemoteName, cancellationToken);
                if (latestVersion == null)
                {
                    _outputWriter.WriteLine($"No version available for '{service.RemoteName}'");
                    return;
                }

                _outputWriter.WriteLine($"Latest version: {latestVersion}");

                // Check if update is needed
                if (currentVersion != null && latestVersion <= currentVersion)
                {
                    _outputWriter.WriteLine("Already up to date");
                    return;
                }

                // Deploy new version (download, find executable, set permissions, update symlink)
                DeployResult? result = await _serviceDeployer.DeployVersionAsync(service, latestVersion, _distributionClient, cancellationToken);
                if (result == null)
                {
                    return;
                }

                // Restart service
                bool serviceExists = await _serviceManager.ServiceExistsAsync(service.LocalName, cancellationToken);
                if (serviceExists)
                {
                    bool isRunning = await _serviceManager.IsServiceRunningAsync(service.LocalName, cancellationToken);
                    if (isRunning)
                    {
                        _outputWriter.WriteLine("Restarting service...");
                        await _serviceManager.RestartServiceAsync(service.LocalName, cancellationToken);
                    }
                    else
                    {
                        _outputWriter.WriteLine("Starting service...");
                        await _serviceManager.StartServiceAsync(service.LocalName, cancellationToken);
                    }

                    _outputWriter.WriteLine("Service updated successfully");
                }
                else
                {
                    _outputWriter.WriteLine("Warning: systemd unit file not found. Service not started.");
                }
            }
            catch (Exception ex)
            {
                _outputWriter.WriteError($"Error updating service: {ex.Message}");
            }
        }

        public string GetDetailedHelp()
        {
            return """
                Update Command

                Usage:
                  updaemon update [app-name]

                Description:
                  Updates all registered services to their latest versions. If an app-name
                  is provided, only that specific service is updated. Services are grouped
                  by distribution plugin and updated efficiently.

                Examples:
                  updaemon update          # Update all services
                  updaemon update my-api  # Update only my-api
                """;
        }
    }
}
