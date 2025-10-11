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
        private readonly string _serviceBaseDirectory;

        public UpdateCommand(
            IConfigManager configManager,
            ISecretsManager secretsManager,
            IServiceManager serviceManager,
            ISymlinkManager symlinkManager,
            IExecutableDetector executableDetector,
            IDistributionServiceClient distributionClient)
        {
            _configManager = configManager;
            _secretsManager = secretsManager;
            _serviceManager = serviceManager;
            _symlinkManager = symlinkManager;
            _executableDetector = executableDetector;
            _distributionClient = distributionClient;
            _serviceBaseDirectory = "/opt";
        }

        public UpdateCommand(
            IConfigManager configManager,
            ISecretsManager secretsManager,
            IServiceManager serviceManager,
            ISymlinkManager symlinkManager,
            IExecutableDetector executableDetector,
            IDistributionServiceClient distributionClient,
            string serviceBaseDirectory)
        {
            _configManager = configManager;
            _secretsManager = secretsManager;
            _serviceManager = serviceManager;
            _symlinkManager = symlinkManager;
            _executableDetector = executableDetector;
            _distributionClient = distributionClient;
            _serviceBaseDirectory = serviceBaseDirectory;
        }

        public async Task ExecuteAsync(string? specificAppName = null)
        {
            // Get the distribution plugin path
            string? pluginPath = await _configManager.GetDistributionPluginPathAsync();
            if (string.IsNullOrEmpty(pluginPath))
            {
                Console.WriteLine("Error: No distribution service plugin configured.");
                Console.WriteLine("Use 'updaemon dist-install <url>' to install a distribution plugin.");
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
                    Console.WriteLine($"Error: Service '{specificAppName}' is not registered.");
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
                Console.WriteLine("No services registered. Use 'updaemon new <app-name>' to create a service.");
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
            Console.WriteLine($"\nUpdating service: {service.LocalName}");

            try
            {
                // Get current version
                Version? currentVersion = await GetCurrentVersionAsync(service.LocalName);
                if (currentVersion != null)
                {
                    Console.WriteLine($"Current version: {currentVersion}");
                }
                else
                {
                    Console.WriteLine("No version currently installed");
                }

                // Get latest version from distribution service
                Version? latestVersion = await _distributionClient.GetLatestVersionAsync(service.RemoteName);
                if (latestVersion == null)
                {
                    Console.WriteLine($"No version available for '{service.RemoteName}'");
                    return;
                }

                Console.WriteLine($"Latest version: {latestVersion}");

                // Check if update is needed
                if (currentVersion != null && latestVersion <= currentVersion)
                {
                    Console.WriteLine("Already up to date");
                    return;
                }

                // Download new version
                string versionDirectory = Path.Combine(_serviceBaseDirectory, service.LocalName, latestVersion.ToString());
                Console.WriteLine($"Downloading to: {versionDirectory}");
                
                Directory.CreateDirectory(versionDirectory);
                await _distributionClient.DownloadVersionAsync(service.RemoteName, latestVersion, versionDirectory);
                Console.WriteLine("Download complete");

                // Find executable
                string? executablePath = await _executableDetector.FindExecutableAsync(versionDirectory, service.LocalName);
                if (executablePath == null)
                {
                    Console.WriteLine($"Error: Could not find executable in {versionDirectory}");
                    return;
                }

                Console.WriteLine($"Found executable: {executablePath}");

                // Update symlink
                string symlinkPath = Path.Combine(_serviceBaseDirectory, service.LocalName, "current");
                await _symlinkManager.CreateOrUpdateSymlinkAsync(symlinkPath, executablePath);
                Console.WriteLine($"Updated symlink: {symlinkPath} -> {executablePath}");

                // Restart service
                bool serviceExists = await _serviceManager.ServiceExistsAsync(service.LocalName);
                if (serviceExists)
                {
                    bool isRunning = await _serviceManager.IsServiceRunningAsync(service.LocalName);
                    if (isRunning)
                    {
                        Console.WriteLine("Restarting service...");
                        await _serviceManager.RestartServiceAsync(service.LocalName);
                    }
                    else
                    {
                        Console.WriteLine("Starting service...");
                        await _serviceManager.StartServiceAsync(service.LocalName);
                    }

                    Console.WriteLine("Service updated successfully");
                }
                else
                {
                    Console.WriteLine("Warning: systemd unit file not found. Service not started.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating service: {ex.Message}");
            }
        }

        private async Task<Version?> GetCurrentVersionAsync(string localName)
        {
            // Check symlink target
            string symlinkPath = Path.Combine(_serviceBaseDirectory, localName, "current");
            string? target = await _symlinkManager.ReadSymlinkAsync(symlinkPath);
            
            if (target != null)
            {
                // Extract version from path (e.g., /opt/app-name/1.2.3/app-name -> 1.2.3)
                char[] separators = new char[] { '/', '\\' };
                string[] parts = target.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                foreach (string part in parts)
                {
                    if (Version.TryParse(part, out Version? version))
                    {
                        return version;
                    }
                }
            }

            // Fallback: scan directory for highest version
            string serviceDirectory = Path.Combine(_serviceBaseDirectory, localName);
            if (!Directory.Exists(serviceDirectory))
            {
                return null;
            }

            Version? highestVersion = null;
            string[] directories = Directory.GetDirectories(serviceDirectory);
            
            foreach (string directory in directories)
            {
                string directoryName = Path.GetFileName(directory);
                if (Version.TryParse(directoryName, out Version? version))
                {
                    if (highestVersion == null || version > highestVersion)
                    {
                        highestVersion = version;
                    }
                }
            }

            return highestVersion;
        }
    }
}

