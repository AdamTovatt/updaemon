using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'set-remote' command to update a service's remote name.
    /// </summary>
    public class SetRemoteCommand
    {
        private readonly IConfigManager _configManager;

        public SetRemoteCommand(IConfigManager configManager)
        {
            _configManager = configManager;
        }

        public async Task ExecuteAsync(string localName, string remoteName)
        {
            Console.WriteLine($"Setting remote name for '{localName}' to '{remoteName}'");

            await _configManager.SetRemoteNameAsync(localName, remoteName);

            Console.WriteLine("Remote name updated successfully");
        }
    }
}

