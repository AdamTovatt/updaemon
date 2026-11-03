using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Parses CLI arguments and executes the appropriate command.
    /// </summary>
    public class CommandExecutor
    {
        private readonly ICommandFactory _commandFactory;
        private readonly IOutputWriter _outputWriter;
        private readonly IRunLock _runLock;
        private readonly UpdaemonVersion _version;
        private readonly IZeroXConverter _converter;
        private readonly UpdaemonScope _scope;

        public CommandExecutor(ICommandFactory commandFactory, IOutputWriter outputWriter, IRunLock runLock, UpdaemonVersion version, IZeroXConverter converter, UpdaemonScope scope)
        {
            _scope = scope;
            _commandFactory = commandFactory;
            _outputWriter = outputWriter;
            _runLock = runLock;
            _version = version;
            _converter = converter;
        }

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            if (args.Length == 0)
            {
                PrintGeneralHelp();
                return 1;
            }

            string commandName = args[0].ToLowerInvariant();

            // Flags in place of a command. Just the version: install.sh and self-update smoke-run a new updaemon with it.
            if (commandName == "--version")
            {
                if (args.Length > 1)
                {
                    _outputWriter.WriteError("Usage: updaemon --version");
                    return 1;
                }

                _outputWriter.WriteLine(_version.ToString());
                return 0;
            }

            if (commandName == "help" || commandName == "--help" || commandName == "-h")
            {
                if (args.Length > 1)
                {
                    if (HelpTopics.Find(args[1].ToLowerInvariant()) is HelpTopics.Topic topic)
                    {
                        // WriteLine ends the guide's last line itself.
                        _outputWriter.WriteLine(HelpTopics.Read(topic).TrimEnd('\n'));
                        return 0;
                    }

                    // Help for specific command: updaemon help update
                    try
                    {
                        ICommand cmd = _commandFactory.GetCommand(args[1].ToLowerInvariant());
                        PrintVersionHeader();
                        _outputWriter.WriteLine(cmd.GetDetailedHelp());
                        return 0;
                    }
                    catch (UnknownCommandException)
                    {
                        _outputWriter.WriteError($"Unknown command: {args[1]}");
                        PrintGeneralHelp();
                        return 1;
                    }
                }
                else
                {
                    PrintGeneralHelp();
                    return 0;
                }
            }

            try
            {
                ICommand command = _commandFactory.GetCommand(commandName);

                // An updaemon 0.x setup is converted once, before any command reads it; 'migrate' is that
                // conversion, and its --dry-run must change nothing.
                bool convertsFirst = command is not MigrateCommand;
                if (!command.ChangesState && !(convertsFirst && _converter.IsPending()))
                {
                    return await command.ExecuteAsync(args[1..], cancellationToken);
                }

                // Scope is never silent where it matters: each run that may change state says whose apps it changes.
                _outputWriter.WriteLine(_scope.Announcement);
                using (await _runLock.AcquireAsync(cancellationToken))
                {
                    // Asked again under the lock: another command may have converted it meanwhile.
                    if (convertsFirst && _converter.IsPending())
                    {
                        await _converter.RunAsync(dryRun: false, cancellationToken);
                    }

                    return await command.ExecuteAsync(args[1..], cancellationToken);
                }
            }
            catch (UnknownCommandException)
            {
                _outputWriter.WriteError($"Unknown command: {commandName}");
                PrintGeneralHelp();
                return 1;
            }
            catch (Exception ex)
            {
                _outputWriter.WriteError($"Error: {ex.Message}");
                return 1;
            }
        }

        private void PrintVersionHeader()
        {
            _outputWriter.WriteLine($"updaemon v{_version}");
            _outputWriter.WriteLine("");
        }

        private void PrintGeneralHelp()
        {
            PrintVersionHeader();
            _outputWriter.WriteLine("Service update daemon");
            _outputWriter.WriteLine("");
            _outputWriter.WriteLine("Usage:");
            _outputWriter.WriteLine("  updaemon <command> [arguments]");
            _outputWriter.WriteLine("  updaemon help <command>    Show detailed help for a command");
            _outputWriter.WriteLine("");
            _outputWriter.WriteLine("Available Commands:");

            // Auto-generate from registered commands!
            foreach (ICommand command in _commandFactory.GetAllCommands())
            {
                _outputWriter.WriteLine($"  {command.Name,-20} {command.Description}");
            }

            _outputWriter.WriteLine("");
            _outputWriter.WriteLine("Guides:");
            foreach (HelpTopics.Topic topic in HelpTopics.All)
            {
                _outputWriter.WriteLine($"  {"help " + topic.Name,-20} {topic.Description}");
            }

            _outputWriter.WriteLine("");
            _outputWriter.WriteLine("Use 'updaemon help <command>' for detailed information about a command.");
        }
    }
}
