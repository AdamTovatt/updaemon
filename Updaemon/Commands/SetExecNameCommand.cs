using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'set-exec-name' command to update a service's executable name.
    /// </summary>
    public class SetExecNameCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;
        private readonly IUnitFileManager _unitFileManager;
        private readonly IServiceManager _serviceManager;
        private readonly string _serviceBaseDirectory;

        public SetExecNameCommand(
            IConfigManager configManager,
            IOutputWriter outputWriter,
            IUnitFileManager unitFileManager,
            IServiceManager serviceManager)
            : this(configManager, outputWriter, unitFileManager, serviceManager, PlatformPaths.ServicesBaseDirectory)
        {
        }

        public SetExecNameCommand(
            IConfigManager configManager,
            IOutputWriter outputWriter,
            IUnitFileManager unitFileManager,
            IServiceManager serviceManager,
            string serviceBaseDirectory)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
            _unitFileManager = unitFileManager;
            _serviceManager = serviceManager;
            _serviceBaseDirectory = serviceBaseDirectory;
        }

        public string Name => "set-exec-name";

        public string Description => "Set executable name for a service";

        public string Usage => "updaemon set-exec-name <app-name> <executable-name>";

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, _outputWriter);

            if (!parser.ValidateMinimumArgs(2, Usage, out int errorCode))
            {
                return errorCode;
            }

            string localName = parser.GetPositional(0)!;
            string executableName = parser.GetPositional(1)!;

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

            try
            {
                await _configManager.SetExecutableNameAsync(localName, executableNameToSet, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _outputWriter.WriteError($"Error: {ex.Message}");
                return 1;
            }

            _outputWriter.WriteLine("Executable name updated successfully");

            // Regenerate unit file if the service is initialized
            string unitFilePath = _unitFileManager.GetUnitFilePath(localName);
            if (File.Exists(unitFilePath))
            {
                string symlinkPath = Path.Combine(_serviceBaseDirectory, localName, "current");
                string effectiveExecName = executableNameToSet ?? localName;

                await _unitFileManager.WriteUnitFileAsync(
                    unitFilePath, localName, symlinkPath, effectiveExecName, cancellationToken);
                await _serviceManager.DaemonReloadAsync(cancellationToken);
                _outputWriter.WriteLine("Updated service unit file");
            }

            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                Set-Exec-Name Command

                Usage:
                  updaemon set-exec-name <app-name> <executable-name>
                  updaemon set-exec-name <app-name> -

                Description:
                  Sets the executable name for a service. This is the name of the executable
                  file that will be searched for in the downloaded version directory. If not
                  set, the local service name is used. Use "-" to clear the executable name.

                Examples:
                  updaemon set-exec-name my-api MyApiExecutable
                  updaemon set-exec-name my-service MyService
                  updaemon set-exec-name my-api -              # Clear executable name
                """;
        }
    }
}
