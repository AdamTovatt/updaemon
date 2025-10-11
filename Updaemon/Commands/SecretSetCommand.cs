using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'secret-set' command to set distribution service secrets.
    /// </summary>
    public class SecretSetCommand
    {
        private readonly ISecretsManager _secretsManager;
        private readonly IOutputWriter _outputWriter;

        public SecretSetCommand(ISecretsManager secretsManager, IOutputWriter outputWriter)
        {
            _secretsManager = secretsManager;
            _outputWriter = outputWriter;
        }

        public async Task ExecuteAsync(string key, string value)
        {
            _outputWriter.WriteLine($"Setting secret: {key}");

            await _secretsManager.SetSecretAsync(key, value);

            _outputWriter.WriteLine("Secret set successfully");
        }
    }
}

