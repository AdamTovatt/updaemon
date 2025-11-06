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

        public async Task ExecuteAsync(string pluginAlias, string key, string value, CancellationToken cancellationToken = default)
        {
            _outputWriter.WriteLine($"Setting secret for plugin '{pluginAlias}': {key}");

            await _secretsManager.SetSecretAsync(pluginAlias, key, value, cancellationToken);

            _outputWriter.WriteLine("Secret set successfully");
        }
    }
}

