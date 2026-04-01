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
        private readonly IOutputWriter _outputWriter;
        private readonly string _serviceBaseDirectory;

        public NewCommand(
            IConfigManager configManager,
            IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
            _serviceBaseDirectory = "/opt";
        }

        public NewCommand(
            IConfigManager configManager,
            IOutputWriter outputWriter,
            string serviceBaseDirectory)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
            _serviceBaseDirectory = serviceBaseDirectory;
        }

        public string Name => "new";

        public string Description => "Register a new service";

        public string Usage => "updaemon new <app-name> --from <plugin-alias> [--remote <remote-name>]";

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

            string? remoteName = parser.GetFlag("--remote");

            // Verify plugin exists
            InstalledPluginInfo? pluginInfo = await _configManager.GetPluginAsync(distributionPluginAlias, cancellationToken);
            if (pluginInfo == null)
            {
                _outputWriter.WriteError($"Distribution plugin '{distributionPluginAlias}' is not installed. Use 'updaemon dist-install' to install a plugin first.");
                return 1;
            }

            _outputWriter.WriteLine($"Registering new service: {appName}");

            // Create the service directory
            string serviceDirectory = Path.Combine(_serviceBaseDirectory, appName);
            Directory.CreateDirectory(serviceDirectory);
            _outputWriter.WriteLine($"Created directory: {serviceDirectory}");

            // Register the service
            string effectiveRemoteName = remoteName ?? appName;
            await _configManager.RegisterServiceAsync(appName, effectiveRemoteName, distributionPluginAlias, cancellationToken);
            _outputWriter.WriteLine($"Registered service in updaemon config");

            _outputWriter.WriteLine($"Service '{appName}' registered successfully!");

            if (remoteName == null)
            {
                _outputWriter.WriteLine($"Note: Remote name defaults to '{appName}'. Use 'updaemon set-remote {appName} <remote-name>' to change it.");
            }

            _outputWriter.WriteLine($"Run 'updaemon init {appName}' to download and set up the service.");
            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                New Command

                Usage:
                  updaemon new <app-name> --from <plugin-alias> [--remote <remote-name>]

                Description:
                  Registers a new service with the specified name. The service will use the
                  specified distribution plugin to check for updates. After registering,
                  run 'updaemon init <app-name>' to download and set up the service.

                Options:
                  --remote <remote-name>  Set the remote name (defaults to app-name)

                Examples:
                  updaemon new my-api --from github
                  updaemon new my-api --from github --remote owner/repo
                  updaemon new my-service --from byteshelf
                """;
        }
    }
}

