using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'set-remote' command to update a service's remote name.
    /// </summary>
    public class SetRemoteCommand
    {
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;

        public SetRemoteCommand(IConfigManager configManager, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
        }

        public async Task ExecuteAsync(string localName, string remoteName)
        {
            _outputWriter.WriteLine($"Setting remote name for '{localName}' to '{remoteName}'");

            await _configManager.SetRemoteNameAsync(localName, remoteName);

            _outputWriter.WriteLine("Remote name updated successfully");
        }
    }
}

