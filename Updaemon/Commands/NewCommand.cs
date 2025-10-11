using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'new' command to create a new service.
    /// </summary>
    public class NewCommand
    {
        private readonly IConfigManager _configManager;
        private readonly IServiceManager _serviceManager;

        public NewCommand(IConfigManager configManager, IServiceManager serviceManager)
        {
            _configManager = configManager;
            _serviceManager = serviceManager;
        }

        public async Task ExecuteAsync(string appName)
        {
            Console.WriteLine($"Creating new service: {appName}");

            // Create the service directory
            string serviceDirectory = $"/opt/{appName}";
            Directory.CreateDirectory(serviceDirectory);
            Console.WriteLine($"Created directory: {serviceDirectory}");

            // Create systemd unit file
            string unitFilePath = $"/etc/systemd/system/{appName}.service";
            string symlinkPath = $"/opt/{appName}/current";
            
            string unitFileContent = GenerateUnitFile(appName, symlinkPath);
            await File.WriteAllTextAsync(unitFilePath, unitFileContent);
            Console.WriteLine($"Created systemd unit file: {unitFilePath}");

            // Register the service (local name = remote name initially)
            await _configManager.RegisterServiceAsync(appName, appName);
            Console.WriteLine($"Registered service in updaemon config");

            // Enable the service
            await _serviceManager.EnableServiceAsync(appName);
            Console.WriteLine($"Enabled service: {appName}");

            Console.WriteLine($"Service '{appName}' created successfully!");
            Console.WriteLine($"Note: Run 'updaemon update {appName}' to download and install the service.");
        }

        private string GenerateUnitFile(string serviceName, string executablePath)
        {
            return $@"[Unit]
Description={serviceName} service managed by updaemon
After=network.target

[Service]
Type=simple
ExecStart={executablePath}
Restart=always
RestartSec=10
StandardOutput=journal
StandardError=journal
SyslogIdentifier={serviceName}

[Install]
WantedBy=multi-user.target
";
        }
    }
}

