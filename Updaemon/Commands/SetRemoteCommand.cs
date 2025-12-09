using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'set-remote' command to update a service's remote name.
    /// </summary>
    public class SetRemoteCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;

        public SetRemoteCommand(IConfigManager configManager, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
        }

        public string Name => "set-remote";

        public string Description => "Set remote name for a service";

        public string Usage => "updaemon set-remote <app-name> <remote-name>";

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, _outputWriter);

            if (!parser.ValidateMinimumArgs(2, Usage, out int errorCode))
            {
                return errorCode;
            }

            string localName = parser.GetPositional(0)!;
            string remoteName = parser.GetPositional(1)!;

            _outputWriter.WriteLine($"Setting remote name for '{localName}' to '{remoteName}'");

            try
            {
                await _configManager.SetRemoteNameAsync(localName, remoteName, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _outputWriter.WriteError($"Error: {ex.Message}");
                return 1;
            }

            _outputWriter.WriteLine("Remote name updated successfully");
            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                Set-Remote Command

                Usage:
                  updaemon set-remote <app-name> <remote-name>

                Description:
                  Sets the remote name for a service. The remote name is used by the
                  distribution plugin to identify the service when checking for updates.

                Examples:
                  updaemon set-remote my-api Dev.MyApi
                  updaemon set-remote my-service Production.MyService
                """;
        }
    }
}

