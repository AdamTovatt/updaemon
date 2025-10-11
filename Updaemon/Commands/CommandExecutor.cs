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
        private readonly DistInstallCommand _distInstallCommand;
        private readonly SecretSetCommand _secretSetCommand;
        private readonly IOutputWriter _outputWriter;

        public CommandExecutor(
            NewCommand newCommand,
            UpdateCommand updateCommand,
            SetRemoteCommand setRemoteCommand,
            DistInstallCommand distInstallCommand,
            SecretSetCommand secretSetCommand,
            IOutputWriter outputWriter)
        {
            _newCommand = newCommand;
            _updateCommand = updateCommand;
            _setRemoteCommand = setRemoteCommand;
            _distInstallCommand = distInstallCommand;
            _secretSetCommand = secretSetCommand;
            _outputWriter = outputWriter;
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
                            _outputWriter.WriteError("Error: 'new' command requires an app name");
                            _outputWriter.WriteLine("Usage: updaemon new <app-name>");
                            return 1;
                        }

                        await _newCommand.ExecuteAsync(args[1], cancellationToken);
                        return 0;

                    case "update":
                        string? appName = args.Length > 1 ? args[1] : null;
                        await _updateCommand.ExecuteAsync(appName, cancellationToken);
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

                    case "dist-install":
                        if (args.Length < 2)
                        {
                            _outputWriter.WriteError("Error: 'dist-install' command requires a URL");
                            _outputWriter.WriteLine("Usage: updaemon dist-install <url>");
                            return 1;
                        }

                        await _distInstallCommand.ExecuteAsync(args[1], cancellationToken);
                        return 0;

                    case "secret-set":
                        if (args.Length < 3)
                        {
                            _outputWriter.WriteError("Error: 'secret-set' command requires a key and value");
                            _outputWriter.WriteLine("Usage: updaemon secret-set <key> <value>");
                            return 1;
                        }

                        await _secretSetCommand.ExecuteAsync(args[1], args[2], cancellationToken);
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
            _outputWriter.WriteLine("  updaemon new <app-name>              Create a new service");
            _outputWriter.WriteLine("  updaemon update [app-name]           Update all services or a specific service");
            _outputWriter.WriteLine("  updaemon set-remote <app> <remote>   Set remote name for a service");
            _outputWriter.WriteLine("  updaemon dist-install <url>          Install a distribution service plugin");
            _outputWriter.WriteLine("  updaemon secret-set <key> <value>    Set a distribution service secret");
            _outputWriter.WriteLine("");
            _outputWriter.WriteLine("Examples:");
            _outputWriter.WriteLine("  updaemon new my-api");
            _outputWriter.WriteLine("  updaemon update");
            _outputWriter.WriteLine("  updaemon update my-api");
            _outputWriter.WriteLine("  updaemon set-remote my-api Dev.MyApi");
            _outputWriter.WriteLine("  updaemon dist-install https://example.com/plugin");
            _outputWriter.WriteLine("  updaemon secret-set apiKey abc123");
        }
    }
}

