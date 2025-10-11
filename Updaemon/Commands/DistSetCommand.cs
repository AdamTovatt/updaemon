using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'dist-set' command to set distribution service secrets.
    /// </summary>
    public class DistSetCommand
    {
        private readonly ISecretsManager _secretsManager;

        public DistSetCommand(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public async Task ExecuteAsync(string key, string value)
        {
            Console.WriteLine($"Setting secret: {key}");

            await _secretsManager.SetSecretAsync(key, value);

            Console.WriteLine("Secret set successfully");
        }
    }
}

