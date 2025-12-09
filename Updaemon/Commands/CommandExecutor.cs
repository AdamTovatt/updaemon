using System.Reflection;
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
        private readonly string _version;

        public CommandExecutor(ICommandFactory commandFactory, IOutputWriter outputWriter)
        {
            _commandFactory = commandFactory;
            _outputWriter = outputWriter;

            // Get version from assembly (AOT-friendly)
            Assembly assembly = Assembly.GetExecutingAssembly();
            _version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                       ?? assembly.GetName().Version?.ToString()
                       ?? "unknown";
        }

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            if (args.Length == 0)
            {
                PrintGeneralHelp();
                return 1;
            }

            string commandName = args[0].ToLowerInvariant();

            // Handle help
            if (commandName == "help" || commandName == "--help" || commandName == "-h")
            {
                if (args.Length > 1)
                {
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
                return await command.ExecuteAsync(args[1..], cancellationToken);
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
            _outputWriter.WriteLine("Use 'updaemon help <command>' for detailed information about a command.");
        }
    }
}
