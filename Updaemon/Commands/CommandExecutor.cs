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
        private readonly DistSetCommand _distSetCommand;

        public CommandExecutor(
            NewCommand newCommand,
            UpdateCommand updateCommand,
            SetRemoteCommand setRemoteCommand,
            DistInstallCommand distInstallCommand,
            DistSetCommand distSetCommand)
        {
            _newCommand = newCommand;
            _updateCommand = updateCommand;
            _setRemoteCommand = setRemoteCommand;
            _distInstallCommand = distInstallCommand;
            _distSetCommand = distSetCommand;
        }

        public async Task<int> ExecuteAsync(string[] args)
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
                            Console.WriteLine("Error: 'new' command requires an app name");
                            Console.WriteLine("Usage: updaemon new <app-name>");
                            return 1;
                        }
                        await _newCommand.ExecuteAsync(args[1]);
                        return 0;

                    case "update":
                        string? appName = args.Length > 1 ? args[1] : null;
                        await _updateCommand.ExecuteAsync(appName);
                        return 0;

                    case "set-remote":
                        if (args.Length < 3)
                        {
                            Console.WriteLine("Error: 'set-remote' command requires app name and remote name");
                            Console.WriteLine("Usage: updaemon set-remote <app-name> <remote-name>");
                            return 1;
                        }
                        await _setRemoteCommand.ExecuteAsync(args[1], args[2]);
                        return 0;

                    case "dist-install":
                        if (args.Length < 2)
                        {
                            Console.WriteLine("Error: 'dist-install' command requires a URL");
                            Console.WriteLine("Usage: updaemon dist-install <url>");
                            return 1;
                        }
                        await _distInstallCommand.ExecuteAsync(args[1]);
                        return 0;

                    case "dist-set":
                        if (args.Length < 3)
                        {
                            Console.WriteLine("Error: 'dist-set' command requires a key and value");
                            Console.WriteLine("Usage: updaemon dist-set <key> <value>");
                            return 1;
                        }
                        await _distSetCommand.ExecuteAsync(args[1], args[2]);
                        return 0;

                    default:
                        Console.WriteLine($"Error: Unknown command '{command}'");
                        PrintUsage();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        private void PrintUsage()
        {
            Console.WriteLine("updaemon - Service update daemon");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  updaemon new <app-name>              Create a new service");
            Console.WriteLine("  updaemon update [app-name]           Update all services or a specific service");
            Console.WriteLine("  updaemon set-remote <app> <remote>   Set remote name for a service");
            Console.WriteLine("  updaemon dist-install <url>          Install a distribution service plugin");
            Console.WriteLine("  updaemon dist-set <key> <value>      Set a distribution service secret");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  updaemon new my-api");
            Console.WriteLine("  updaemon update");
            Console.WriteLine("  updaemon update my-api");
            Console.WriteLine("  updaemon set-remote my-api Dev.MyApi");
            Console.WriteLine("  updaemon dist-install https://example.com/plugin");
            Console.WriteLine("  updaemon dist-set apiKey abc123");
        }
    }
}

