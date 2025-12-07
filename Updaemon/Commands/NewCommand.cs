using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'new' command to create a new service.
    /// </summary>
    public class NewCommand : ICommand
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

        public string Name => "new";

        public string Description => "Create a new service";

        public string Usage => "updaemon new <app-name> --from <plugin-alias>";

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, _outputWriter);

            if (!parser.TryGetRequiredPositional(0, "app-name", out string appName, out int errorCode))
            {
                _outputWriter.WriteLine(Usage);
                return errorCode;
            }

            if (!parser.TryGetRequiredFlag("--from", out string distributionPluginAlias, out errorCode))
            {
                _outputWriter.WriteLine(Usage);
                return errorCode;
            }

            // Verify plugin exists
            InstalledPluginInfo? pluginInfo = await _configManager.GetPluginAsync(distributionPluginAlias, cancellationToken);
            if (pluginInfo == null)
            {
                _outputWriter.WriteError($"Distribution plugin '{distributionPluginAlias}' is not installed. Use 'updaemon dist-install' to install a plugin first.");
                return 1;
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
            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                New Command

                Usage:
                  updaemon new <app-name> --from <plugin-alias>

                Description:
                  Creates a new service with the specified name. The service will use the
                  specified distribution plugin to check for updates. A systemd unit file
                  is created and the service is registered in the updaemon configuration.

                Examples:
                  updaemon new my-api --from github
                  updaemon new my-service --from byteshelf
                """;
        }
    }
}

