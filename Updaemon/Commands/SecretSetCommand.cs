using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'secret-set' command to set distribution service secrets.
    /// </summary>
    public class SecretSetCommand : ICommand
    {
        private readonly ISecretsManager _secretsManager;
        private readonly IOutputWriter _outputWriter;

        public SecretSetCommand(ISecretsManager secretsManager, IOutputWriter outputWriter)
        {
            _secretsManager = secretsManager;
            _outputWriter = outputWriter;
        }

        public string Name => "secret-set";

        public string Description => "Set a secret for a plugin";

        public string Usage => "updaemon secret-set <plugin-alias> <key> <value>";

        public bool ChangesState => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args);
            if (parser.Positionals is not [string pluginAlias, string key, string value])
            {
                return _outputWriter.FailUsage(this);
            }

            _outputWriter.WriteLine($"Setting secret for plugin '{pluginAlias}': {key}");

            await _secretsManager.SetSecretAsync(pluginAlias, key, value, cancellationToken);

            _outputWriter.WriteLine("Secret set successfully");
            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                Secret-Set Command

                Usage:
                  updaemon secret-set <plugin-alias> <key> <value>

                Description:
                  Sets a secret value for a distribution plugin. Secrets are used by
                  plugins to authenticate or configure access to distribution services.

                Examples:
                  updaemon secret-set github githubToken abc123
                  updaemon secret-set byteshelf apiKey xyz789
                """;
        }
    }
}

