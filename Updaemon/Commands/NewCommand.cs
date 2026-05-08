using Updaemon.Configuration;
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
            : this(configManager, outputWriter, PlatformPaths.ServicesBaseDirectory)
        {
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

        public string Description => "Register a new service or CLI tool";

        public string Usage => "updaemon new <app-name> --from <plugin-alias> [--remote <remote-name>] [--type <service|cli>]";

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

            string? typeFlag = parser.GetFlag("--type");
            ServiceType serviceType = ServiceType.Service;
            if (typeFlag != null)
            {
                if (string.Equals(typeFlag, "cli", StringComparison.OrdinalIgnoreCase))
                {
                    serviceType = ServiceType.Cli;
                }
                else if (!string.Equals(typeFlag, "service", StringComparison.OrdinalIgnoreCase))
                {
                    _outputWriter.WriteError($"Error: Invalid type '{typeFlag}'. Must be 'service' or 'cli'.");
                    return 1;
                }
            }

            // Verify plugin exists
            InstalledPluginInfo? pluginInfo = await _configManager.GetPluginAsync(distributionPluginAlias, cancellationToken);
            if (pluginInfo == null)
            {
                _outputWriter.WriteError($"Distribution plugin '{distributionPluginAlias}' is not installed. Use 'updaemon dist-install' to install a plugin first.");
                return 1;
            }

            string typeLabel = serviceType.ToLabel();
            _outputWriter.WriteLine($"Registering new {typeLabel}: {appName}");

            // Create the service directory
            string serviceDirectory = Path.Combine(_serviceBaseDirectory, appName);
            Directory.CreateDirectory(serviceDirectory);
            _outputWriter.WriteLine($"Created directory: {serviceDirectory}");

            // Register the service
            string effectiveRemoteName = remoteName ?? appName;
            await _configManager.RegisterServiceAsync(appName, effectiveRemoteName, distributionPluginAlias, serviceType, cancellationToken);
            _outputWriter.WriteLine($"Registered {typeLabel} in updaemon config");

            _outputWriter.WriteLine($"{typeLabel} '{appName}' registered successfully!");

            if (remoteName == null)
            {
                _outputWriter.WriteLine($"Note: Remote name defaults to '{appName}'. Use 'updaemon set-remote {appName} <remote-name>' to change it.");
            }

            _outputWriter.WriteLine($"Run 'updaemon init {appName}' to download and set up the {typeLabel}.");
            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                New Command

                Usage:
                  updaemon new <app-name> --from <plugin-alias> [--remote <remote-name>] [--type <service|cli>]

                Description:
                  Registers a new service or CLI tool with the specified name. The entry will
                  use the specified distribution plugin to check for updates. After registering,
                  run 'updaemon init <app-name>' to download and set it up.

                Options:
                  --remote <remote-name>       Set the remote name (defaults to app-name)
                  --type <service|cli>         Set the type (defaults to service)
                                               service: managed as a long-running daemon
                                                        (systemd on Linux, launchd on macOS)
                                               cli: symlinked into /usr/local/bin

                Examples:
                  updaemon new my-api --from github
                  updaemon new my-api --from github --remote owner/repo
                  updaemon new ripgrep --from github --remote BurntSushi/ripgrep --type cli
                """;
        }
    }
}

