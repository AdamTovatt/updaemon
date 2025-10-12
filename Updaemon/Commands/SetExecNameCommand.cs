using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'set-exec-name' command to update a service's executable name.
    /// </summary>
    public class SetExecNameCommand
    {
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;

        public SetExecNameCommand(IConfigManager configManager, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
        }

        public async Task ExecuteAsync(string localName, string executableName, CancellationToken cancellationToken = default)
        {
            // Handle "-" as a special value to clear the executable name
            string? executableNameToSet = executableName == "-" ? null : executableName;

            if (executableNameToSet == null)
            {
                _outputWriter.WriteLine($"Clearing executable name for '{localName}' (will use local name)");
            }
            else
            {
                _outputWriter.WriteLine($"Setting executable name for '{localName}' to '{executableNameToSet}'");
            }

            await _configManager.SetExecutableNameAsync(localName, executableNameToSet, cancellationToken);

            _outputWriter.WriteLine("Executable name updated successfully");
        }
    }
}

