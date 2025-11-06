using Updaemon.Interfaces;
using Updaemon.Models;

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
        private readonly IUnitFileManager _unitFileManager;
        private readonly string _serviceBaseDirectory;
        private readonly string _systemdUnitDirectory;

        public NewCommand(
            IConfigManager configManager,
            IServiceManager serviceManager,
            IOutputWriter outputWriter,
            IUnitFileManager unitFileManager)
        {
            _configManager = configManager;
            _serviceManager = serviceManager;
            _outputWriter = outputWriter;
            _unitFileManager = unitFileManager;
            _serviceBaseDirectory = "/opt";
            _systemdUnitDirectory = "/etc/systemd/system";
        }

        public NewCommand(
            IConfigManager configManager,
            IServiceManager serviceManager,
            IOutputWriter outputWriter,
            IUnitFileManager unitFileManager,
            string serviceBaseDirectory,
            string systemdUnitDirectory)
        {
            _configManager = configManager;
            _serviceManager = serviceManager;
            _outputWriter = outputWriter;
            _unitFileManager = unitFileManager;
            _serviceBaseDirectory = serviceBaseDirectory;
            _systemdUnitDirectory = systemdUnitDirectory;
        }

        public async Task ExecuteAsync(string appName, string distributionPluginAlias, CancellationToken cancellationToken = default)
        {
            // Verify plugin exists
            InstalledPluginInfo? pluginInfo = await _configManager.GetPluginAsync(distributionPluginAlias, cancellationToken);
            if (pluginInfo == null)
            {
                throw new InvalidOperationException($"Distribution plugin '{distributionPluginAlias}' is not installed. Use 'updaemon dist-install' to install a plugin first.");
            }

            _outputWriter.WriteLine($"Creating new service: {appName}");

            // Create the service directory
            string serviceDirectory = Path.Combine(_serviceBaseDirectory, appName);
            Directory.CreateDirectory(serviceDirectory);
            _outputWriter.WriteLine($"Created directory: {serviceDirectory}");

            // Create systemd unit file
            string unitFilePath = Path.Combine(_systemdUnitDirectory, $"{appName}.service");
            string symlinkPath = Path.Combine(_serviceBaseDirectory, appName, "current");

            string unitFileContent = await _unitFileManager.ReadTemplateWithSubstitutionsAsync(appName, symlinkPath, appName, cancellationToken);
            await File.WriteAllTextAsync(unitFilePath, unitFileContent, cancellationToken);
            _outputWriter.WriteLine($"Created systemd unit file: {unitFilePath}");

            // Register the service (local name = remote name initially)
            await _configManager.RegisterServiceAsync(appName, appName, distributionPluginAlias, cancellationToken);
            _outputWriter.WriteLine($"Registered service in updaemon config");

            // Enable the service
            await _serviceManager.EnableServiceAsync(appName, cancellationToken);
            _outputWriter.WriteLine($"Enabled service: {appName}");

            _outputWriter.WriteLine($"Service '{appName}' created successfully!");
            _outputWriter.WriteLine($"Note: Run 'updaemon update {appName}' to download and install the service.");
        }
    }
}

