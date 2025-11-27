using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Parses CLI arguments and executes the appropriate command.
    /// </summary>
    public class CommandExecutor
    {
        private readonly NewCommand _newCommand;
        private readonly UpdateCommand _updateCommand;
        private readonly SetRemoteCommand _setRemoteCommand;
        private readonly SetExecNameCommand _setExecNameCommand;
        private readonly DistInstallCommand _distInstallCommand;
        private readonly DistListCommand _distListCommand;
        private readonly SecretSetCommand _secretSetCommand;
        private readonly TimerCommand _timerCommand;
        private readonly IOutputWriter _outputWriter;
        private readonly IPluginUrlResolver _pluginUrlResolver;

        public CommandExecutor(
            NewCommand newCommand,
            UpdateCommand updateCommand,
            SetRemoteCommand setRemoteCommand,
            SetExecNameCommand setExecNameCommand,
            DistInstallCommand distInstallCommand,
            DistListCommand distListCommand,
            SecretSetCommand secretSetCommand,
            TimerCommand timerCommand,
            IOutputWriter outputWriter,
            IPluginUrlResolver pluginUrlResolver)
        {
            _newCommand = newCommand;
            _updateCommand = updateCommand;
            _setRemoteCommand = setRemoteCommand;
            _setExecNameCommand = setExecNameCommand;
            _distInstallCommand = distInstallCommand;
            _distListCommand = distListCommand;
            _secretSetCommand = secretSetCommand;
            _timerCommand = timerCommand;
            _outputWriter = outputWriter;
            _pluginUrlResolver = pluginUrlResolver;
        }

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            string command = args[0].ToLowerInvariant();

            try
            {
                switch (command)
                {
                    case "new":
                        if (args.Length < 2)
                        {
                            _outputWriter.WriteError("Error: 'new' command requires an app name and --from flag");
                            _outputWriter.WriteLine("Usage: updaemon new <app-name> --from <plugin-alias>");
                            return 1;
                        }

                        // Parse --from flag
                        string appName = args[1];
                        string? pluginAlias = null;

                        for (int i = 2; i < args.Length; i++)
                        {
                            if (args[i] == "--from" && i + 1 < args.Length)
                            {
                                pluginAlias = args[i + 1];
                                break;
                            }
                        }

                        if (string.IsNullOrEmpty(pluginAlias))
                        {
                            _outputWriter.WriteError("Error: 'new' command requires --from flag");
                            _outputWriter.WriteLine("Usage: updaemon new <app-name> --from <plugin-alias>");
                            return 1;
                        }

                        await _newCommand.ExecuteAsync(appName, pluginAlias, cancellationToken);
                        return 0;

                    case "update":
                        string? updateAppName = args.Length > 1 ? args[1] : null;
                        await _updateCommand.ExecuteAsync(updateAppName, cancellationToken);
                        return 0;

                    case "set-remote":
                        if (args.Length < 3)
                        {
                            _outputWriter.WriteError("Error: 'set-remote' command requires app name and remote name");
                            _outputWriter.WriteLine("Usage: updaemon set-remote <app-name> <remote-name>");
                            return 1;
                        }

                        await _setRemoteCommand.ExecuteAsync(args[1], args[2], cancellationToken);
                        return 0;

                    case "set-exec-name":
                        if (args.Length < 3)
                        {
                            _outputWriter.WriteError("Error: 'set-exec-name' command requires app name and executable name");
                            _outputWriter.WriteLine("Usage: updaemon set-exec-name <app-name> <executable-name>");
                            _outputWriter.WriteLine("Use '-' as executable name to clear it");
                            return 1;
                        }

                        await _setExecNameCommand.ExecuteAsync(args[1], args[2], cancellationToken);
                        return 0;

                    case "dist-install":
                        if (args.Length < 2)
                        {
                            _outputWriter.WriteError("Error: 'dist-install' command requires a plugin name or URL");
                            _outputWriter.WriteLine("Usage: updaemon dist-install [--as <alias>] <plugin-name|url>");
                            return 1;
                        }

                        // Parse --as flag
                        string? alias = null;
                        string? urlOrName = null;

                        for (int i = 1; i < args.Length; i++)
                        {
                            if (args[i] == "--as" && i + 1 < args.Length)
                            {
                                alias = args[i + 1];
                                i++; // Skip the alias value
                            }
                            else if (!args[i].StartsWith("--"))
                            {
                                urlOrName = args[i];
                            }
                        }

                        if (string.IsNullOrEmpty(urlOrName))
                        {
                            _outputWriter.WriteError("Error: 'dist-install' command requires a plugin name or URL");
                            _outputWriter.WriteLine("Usage: updaemon dist-install [--as <alias>] <plugin-name|url>");
                            return 1;
                        }

                        // Determine if input is a URL or a plugin name
                        string finalUrl = urlOrName;
                        if (!urlOrName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                            !urlOrName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            // It's a plugin name, resolve it to a URL
                            try
                            {
                                alias = urlOrName;
                                finalUrl = await _pluginUrlResolver.ResolveAsync(urlOrName, cancellationToken);
                            }
                            catch (InvalidOperationException)
                            {
                                // Re-throw to preserve the helpful error message from the resolver
                                throw;
                            }
                        }

                        await _distInstallCommand.ExecuteAsync(alias, finalUrl, cancellationToken);
                        return 0;

                    case "dist-list":
                        await _distListCommand.ExecuteAsync(cancellationToken);
                        return 0;

                    case "secret-set":
                        if (args.Length < 4)
                        {
                            _outputWriter.WriteError("Error: 'secret-set' command requires plugin alias, key, and value");
                            _outputWriter.WriteLine("Usage: updaemon secret-set <plugin-alias> <key> <value>");
                            return 1;
                        }

                        await _secretSetCommand.ExecuteAsync(args[1], args[2], args[3], cancellationToken);
                        return 0;

                    case "timer":
                        string? interval = args.Length > 1 ? args[1] : null;
                        await _timerCommand.ExecuteAsync(interval, cancellationToken);
                        return 0;

                    default:
                        _outputWriter.WriteError($"Error: Unknown command '{command}'");
                        PrintUsage();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                _outputWriter.WriteError($"Error: {ex.Message}");
                return 1;
            }
        }

        private void PrintUsage()
        {
            _outputWriter.WriteLine("updaemon - Service update daemon");
            _outputWriter.WriteLine("");
            _outputWriter.WriteLine("Usage:");
            _outputWriter.WriteLine("  updaemon new <app-name> --from <plugin>    Create a new service");
            _outputWriter.WriteLine("  updaemon update [app-name]                Update all services or a specific service");
            _outputWriter.WriteLine("  updaemon set-remote <app> <remote>        Set remote name for a service");
            _outputWriter.WriteLine("  updaemon set-exec-name <app> <exec-name>  Set executable name for a service");
            _outputWriter.WriteLine("  updaemon dist-install [--as <alias>] <plugin-name|url> Install a distribution service plugin");
            _outputWriter.WriteLine("  updaemon dist-list                        List installed distribution plugins");
            _outputWriter.WriteLine("  updaemon secret-set <plugin> <key> <value> Set a secret for a plugin");
            _outputWriter.WriteLine("  updaemon timer [interval]                 Manage automatic update timer");
            _outputWriter.WriteLine("");
            _outputWriter.WriteLine("Examples:");
            _outputWriter.WriteLine("  updaemon new my-api --from github");
            _outputWriter.WriteLine("  updaemon update");
            _outputWriter.WriteLine("  updaemon update my-api");
            _outputWriter.WriteLine("  updaemon set-remote my-api Dev.MyApi");
            _outputWriter.WriteLine("  updaemon set-exec-name my-api MyApiExecutable");
            _outputWriter.WriteLine("  updaemon set-exec-name my-api -");
            _outputWriter.WriteLine("  updaemon dist-install github");
            _outputWriter.WriteLine("  updaemon dist-install --as github https://example.com/plugin");
            _outputWriter.WriteLine("  updaemon dist-install https://example.com/plugin");
            _outputWriter.WriteLine("  updaemon dist-list");
            _outputWriter.WriteLine("  updaemon secret-set github githubToken abc123");
            _outputWriter.WriteLine("  updaemon timer 10m");
            _outputWriter.WriteLine("  updaemon timer 1h");
            _outputWriter.WriteLine("  updaemon timer -");
        }
    }
}

