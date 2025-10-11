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
        private readonly IOutputWriter _outputWriter;
        private readonly string _serviceBaseDirectory;
        private readonly string _systemdUnitDirectory;

        public NewCommand(
            IConfigManager configManager,
            IServiceManager serviceManager,
            IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _serviceManager = serviceManager;
            _outputWriter = outputWriter;
            _serviceBaseDirectory = "/opt";
            _systemdUnitDirectory = "/etc/systemd/system";
        }

        public NewCommand(
            IConfigManager configManager,
            IServiceManager serviceManager,
            IOutputWriter outputWriter,
            string serviceBaseDirectory,
            string systemdUnitDirectory)
        {
            _configManager = configManager;
            _serviceManager = serviceManager;
            _outputWriter = outputWriter;
            _serviceBaseDirectory = serviceBaseDirectory;
            _systemdUnitDirectory = systemdUnitDirectory;
        }

        public async Task ExecuteAsync(string appName)
        {
            _outputWriter.WriteLine($"Creating new service: {appName}");

            // Create the service directory
            string serviceDirectory = Path.Combine(_serviceBaseDirectory, appName);
            Directory.CreateDirectory(serviceDirectory);
            _outputWriter.WriteLine($"Created directory: {serviceDirectory}");

            // Create systemd unit file
            string unitFilePath = Path.Combine(_systemdUnitDirectory, $"{appName}.service");
            string symlinkPath = Path.Combine(_serviceBaseDirectory, appName, "current");
            
            string unitFileContent = GenerateUnitFile(appName, symlinkPath);
            await File.WriteAllTextAsync(unitFilePath, unitFileContent);
            _outputWriter.WriteLine($"Created systemd unit file: {unitFilePath}");

            // Register the service (local name = remote name initially)
            await _configManager.RegisterServiceAsync(appName, appName);
            _outputWriter.WriteLine($"Registered service in updaemon config");

            // Enable the service
            await _serviceManager.EnableServiceAsync(appName);
            _outputWriter.WriteLine($"Enabled service: {appName}");

            _outputWriter.WriteLine($"Service '{appName}' created successfully!");
            _outputWriter.WriteLine($"Note: Run 'updaemon update {appName}' to download and install the service.");
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

