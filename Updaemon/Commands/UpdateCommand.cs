using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'update' command to update services.
    /// </summary>
    public class UpdateCommand
    {
        private readonly IConfigManager _configManager;
        private readonly ISecretsManager _secretsManager;
        private readonly IServiceManager _serviceManager;
        private readonly ISymlinkManager _symlinkManager;
        private readonly IExecutableDetector _executableDetector;
        private readonly IDistributionServiceClient _distributionClient;
        private readonly IOutputWriter _outputWriter;
        private readonly IVersionExtractor _versionExtractor;
        private readonly string _serviceBaseDirectory;

        public UpdateCommand(
            IConfigManager configManager,
            ISecretsManager secretsManager,
            IServiceManager serviceManager,
            ISymlinkManager symlinkManager,
            IExecutableDetector executableDetector,
            IDistributionServiceClient distributionClient,
            IOutputWriter outputWriter,
            IVersionExtractor versionExtractor)
        {
            _configManager = configManager;
            _secretsManager = secretsManager;
            _serviceManager = serviceManager;
            _symlinkManager = symlinkManager;
            _executableDetector = executableDetector;
            _distributionClient = distributionClient;
            _outputWriter = outputWriter;
            _versionExtractor = versionExtractor;
            _serviceBaseDirectory = "/opt";
        }

        public UpdateCommand(
            IConfigManager configManager,
            ISecretsManager secretsManager,
            IServiceManager serviceManager,
            ISymlinkManager symlinkManager,
            IExecutableDetector executableDetector,
            IDistributionServiceClient distributionClient,
            IOutputWriter outputWriter,
            IVersionExtractor versionExtractor,
            string serviceBaseDirectory)
        {
            _configManager = configManager;
            _secretsManager = secretsManager;
            _serviceManager = serviceManager;
            _symlinkManager = symlinkManager;
            _executableDetector = executableDetector;
            _distributionClient = distributionClient;
            _outputWriter = outputWriter;
            _versionExtractor = versionExtractor;
            _serviceBaseDirectory = serviceBaseDirectory;
        }

        public async Task ExecuteAsync(string? specificAppName = null)
        {
            // Get the distribution plugin path
            string? pluginPath = await _configManager.GetDistributionPluginPathAsync();
            if (string.IsNullOrEmpty(pluginPath))
            {
                _outputWriter.WriteError("Error: No distribution service plugin configured.");
                _outputWriter.WriteLine("Use 'updaemon dist-install <url>' to install a distribution plugin.");
                return;
            }

            // Connect to the distribution service
            await _distributionClient.ConnectAsync(pluginPath);

            // Initialize with secrets
            string? secrets = await _secretsManager.GetAllSecretsFormattedAsync();
            await _distributionClient.InitializeAsync(secrets);

            // Get services to update
            IReadOnlyList<RegisteredService> services;
            if (specificAppName != null)
            {
                RegisteredService? service = await _configManager.GetServiceAsync(specificAppName);
                if (service == null)
                {
                    _outputWriter.WriteError($"Error: Service '{specificAppName}' is not registered.");
                    return;
                }
                services = new[] { service };
            }
            else
            {
                services = await _configManager.GetAllServicesAsync();
            }

            if (services.Count == 0)
            {
                _outputWriter.WriteLine("No services registered. Use 'updaemon new <app-name>' to create a service.");
                return;
            }

            // Update each service
            foreach (RegisteredService service in services)
            {
                await UpdateServiceAsync(service);
            }
        }

        private async Task UpdateServiceAsync(RegisteredService service)
        {
            _outputWriter.WriteLine($"\nUpdating service: {service.LocalName}");

            try
            {
                // Get current version
                Version? currentVersion = await GetCurrentVersionAsync(service.LocalName);
                if (currentVersion != null)
                {
                    _outputWriter.WriteLine($"Current version: {currentVersion}");
                }
                else
                {
                    _outputWriter.WriteLine("No version currently installed");
                }

                // Get latest version from distribution service
                Version? latestVersion = await _distributionClient.GetLatestVersionAsync(service.RemoteName);
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

                // Download new version
                string versionDirectory = Path.Combine(_serviceBaseDirectory, service.LocalName, latestVersion.ToString());
                _outputWriter.WriteLine($"Downloading to: {versionDirectory}");

                Directory.CreateDirectory(versionDirectory);
                await _distributionClient.DownloadVersionAsync(service.RemoteName, latestVersion, versionDirectory);
                _outputWriter.WriteLine("Download complete");

                // Find executable
                string? executablePath = await _executableDetector.FindExecutableAsync(versionDirectory, service.LocalName);
                if (executablePath == null)
                {
                    _outputWriter.WriteError($"Error: Could not find executable in {versionDirectory}");
                    return;
                }

                _outputWriter.WriteLine($"Found executable: {executablePath}");

                // Update symlink
                string symlinkPath = Path.Combine(_serviceBaseDirectory, service.LocalName, "current");
                await _symlinkManager.CreateOrUpdateSymlinkAsync(symlinkPath, executablePath);
                _outputWriter.WriteLine($"Updated symlink: {symlinkPath} -> {executablePath}");

                // Restart service
                bool serviceExists = await _serviceManager.ServiceExistsAsync(service.LocalName);
                if (serviceExists)
                {
                    bool isRunning = await _serviceManager.IsServiceRunningAsync(service.LocalName);
                    if (isRunning)
                    {
                        _outputWriter.WriteLine("Restarting service...");
                        await _serviceManager.RestartServiceAsync(service.LocalName);
                    }
                    else
                    {
                        _outputWriter.WriteLine("Starting service...");
                        await _serviceManager.StartServiceAsync(service.LocalName);
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

        private async Task<Version?> GetCurrentVersionAsync(string localName)
        {
            // Check symlink target
            string symlinkPath = Path.Combine(_serviceBaseDirectory, localName, "current");
            string? target = await _symlinkManager.ReadSymlinkAsync(symlinkPath);

            return _versionExtractor.ExtractVersionFromPath(target);
        }
    }
}

