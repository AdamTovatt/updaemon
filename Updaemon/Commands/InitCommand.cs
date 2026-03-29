using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'init' command to perform first-time download and setup of a registered service.
    /// </summary>
    public class InitCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly ISecretsManager _secretsManager;
        private readonly IServiceManager _serviceManager;
        private readonly IDistributionServiceClient _distributionClient;
        private readonly IOutputWriter _outputWriter;
        private readonly IUnitFileManager _unitFileManager;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly string _systemdUnitDirectory;

        public InitCommand(
            IConfigManager configManager,
            ISecretsManager secretsManager,
            IServiceManager serviceManager,
            IDistributionServiceClient distributionClient,
            IOutputWriter outputWriter,
            IUnitFileManager unitFileManager,
            IServiceDeployer serviceDeployer)
        {
            _configManager = configManager;
            _secretsManager = secretsManager;
            _serviceManager = serviceManager;
            _distributionClient = distributionClient;
            _outputWriter = outputWriter;
            _unitFileManager = unitFileManager;
            _serviceDeployer = serviceDeployer;
            _systemdUnitDirectory = "/etc/systemd/system";
        }

        public InitCommand(
            IConfigManager configManager,
            ISecretsManager secretsManager,
            IServiceManager serviceManager,
            IDistributionServiceClient distributionClient,
            IOutputWriter outputWriter,
            IUnitFileManager unitFileManager,
            IServiceDeployer serviceDeployer,
            string systemdUnitDirectory)
        {
            _configManager = configManager;
            _secretsManager = secretsManager;
            _serviceManager = serviceManager;
            _distributionClient = distributionClient;
            _outputWriter = outputWriter;
            _unitFileManager = unitFileManager;
            _serviceDeployer = serviceDeployer;
            _systemdUnitDirectory = systemdUnitDirectory;
        }

        public string Name => "init";

        public string Description => "Download and set up a registered service for the first time";

        public string Usage => "updaemon init <app-name>";

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, _outputWriter);

            if (!parser.TryGetRequiredPositional(0, "app-name", out string appName, out int errorCode))
            {
                _outputWriter.WriteLine(Usage);
                return errorCode;
            }

            // Look up service in config
            RegisteredService? service = await _configManager.GetServiceAsync(appName, cancellationToken);
            if (service == null)
            {
                _outputWriter.WriteError($"Error: Service '{appName}' is not registered. Use 'updaemon new' to register it first.");
                return 1;
            }

            // Check if already initialized
            string? existingTarget = await _serviceDeployer.ReadCurrentTargetAsync(service.LocalName, cancellationToken);
            if (existingTarget != null)
            {
                _outputWriter.WriteLine($"Service '{appName}' is already initialized.");
                return 0;
            }

            // Verify plugin exists
            InstalledPluginInfo? pluginInfo = await _configManager.GetPluginAsync(service.DistributionPluginAlias, cancellationToken);
            if (pluginInfo == null)
            {
                _outputWriter.WriteError($"Error: Plugin '{service.DistributionPluginAlias}' not found.");
                return 1;
            }

            if (!File.Exists(pluginInfo.Path))
            {
                _outputWriter.WriteError($"Error: Plugin executable not found at '{pluginInfo.Path}'.");
                return 1;
            }

            // Pre-flight: check write access to systemd unit directory
            if (!Directory.Exists(_systemdUnitDirectory))
            {
                _outputWriter.WriteError($"Error: Systemd unit directory '{_systemdUnitDirectory}' does not exist.");
                return 1;
            }

            string testFilePath = Path.Combine(_systemdUnitDirectory, $".updaemon-init-check-{Guid.NewGuid()}");
            try
            {
                await File.WriteAllTextAsync(testFilePath, "", cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                _outputWriter.WriteError($"Error: No write access to '{_systemdUnitDirectory}'. Run with appropriate permissions.");
                return 1;
            }
            finally
            {
                try { File.Delete(testFilePath); } catch { }
            }

            _outputWriter.WriteLine($"Initializing service: {appName}");

            // Connect to plugin
            await _distributionClient.ConnectAsync(pluginInfo.Path, cancellationToken);
            string? secrets = await _secretsManager.GetPluginSecretsFormattedAsync(service.DistributionPluginAlias, cancellationToken);
            await _distributionClient.InitializeAsync(secrets, cancellationToken);

            DeployResult? deployResult = null;
            string? unitFilePath = null;

            try
            {
                // Get latest version
                Version? latestVersion = await _distributionClient.GetLatestVersionAsync(service.RemoteName, cancellationToken);
                if (latestVersion == null)
                {
                    _outputWriter.WriteError($"Error: No version available for '{service.RemoteName}'.");
                    return 1;
                }

                _outputWriter.WriteLine($"Latest version: {latestVersion}");

                // Deploy (download, find executable, set permissions, create symlink)
                deployResult = await _serviceDeployer.DeployVersionAsync(service, latestVersion, _distributionClient, cancellationToken);
                if (deployResult == null)
                {
                    return 1;
                }

                // Generate and write unit file
                string detectedExecutableName = Path.GetFileName(deployResult.ExecutablePath);
                unitFilePath = Path.Combine(_systemdUnitDirectory, $"{service.LocalName}.service");

                await _unitFileManager.WriteUnitFileAsync(
                    unitFilePath, service.LocalName, deployResult.SymlinkPath, detectedExecutableName, cancellationToken);
                _outputWriter.WriteLine($"Created systemd unit file: {unitFilePath}");

                // Reload systemd and enable/start service
                await _serviceManager.DaemonReloadAsync(cancellationToken);
                await _serviceManager.EnableServiceAsync(service.LocalName, cancellationToken);
                await _serviceManager.StartServiceAsync(service.LocalName, cancellationToken);
                _outputWriter.WriteLine($"Service '{appName}' initialized and started successfully!");

                return 0;
            }
            catch (Exception ex)
            {
                _outputWriter.WriteError($"Error during initialization: {ex.Message}");

                // Clean up unit file
                if (unitFilePath != null)
                {
                    try { File.Delete(unitFilePath); } catch { }
                }

                // Clean up deploy artifacts (version directory + symlink)
                if (deployResult != null)
                {
                    await _serviceDeployer.CleanupDeployAsync(deployResult, cancellationToken);
                    _outputWriter.WriteLine("Cleaned up deploy artifacts");
                }

                return 1;
            }
            finally
            {
                await _distributionClient.DisposeAsync();
            }
        }

        public string GetDetailedHelp()
        {
            return """
                Init Command

                Usage:
                  updaemon init <app-name>

                Description:
                  Downloads and sets up a registered service for the first time. This command
                  downloads the latest version, detects the executable, creates the systemd
                  unit file with the correct ExecStart path, and starts the service.

                  The service must first be registered with 'updaemon new'. If the service
                  is already initialized, this command does nothing.

                Examples:
                  updaemon init my-api
                  updaemon init my-service
                """;
        }
    }
}
