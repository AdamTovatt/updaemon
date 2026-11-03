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
        private readonly IPrompter _prompter;

        public SecretSetCommand(ISecretsManager secretsManager, IOutputWriter outputWriter, IPrompter prompter)
        {
            _secretsManager = secretsManager;
            _outputWriter = outputWriter;
            _prompter = prompter;
        }

        public string Name => "secret-set";

        public string Description => "Set a secret for a plugin";

        public string Usage => "updaemon secret-set <plugin-alias> <key> [<value>]";

        public bool ChangesState => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args);
            if (parser.Positionals.Count is < 2 or > 3)
            {
                return _outputWriter.FailUsage(this);
            }

            string pluginAlias = parser.Positionals[0];
            string key = parser.Positionals[1];
            string value;
            if (parser.Positionals is [_, _, string given])
            {
                value = given;
            }
            else if (!_prompter.IsInteractive)
            {
                return _outputWriter.Fail($"No value for {key}, and no terminal to ask for it. Give it after the key: {Usage}");
            }
            else
            {
                // Asked without echo, so the value stays out of the shell's history.
                value = (await _prompter.ReadValueAsync($"{key} for the '{pluginAlias}' plugin (not shown): ", secret: true, cancellationToken)).Trim();
                if (value.Length == 0)
                {
                    return _outputWriter.Fail($"No value was given; {key} is unchanged.");
                }
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
                  updaemon secret-set <plugin-alias> <key> [<value>]

                Description:
                  Sets a secret value for a distribution plugin. Secrets are used by
                  plugins to authenticate or configure access to distribution services.
                  Without a value, it asks for one without showing it, which keeps it
                  out of the shell's history.

                Examples:
                  updaemon secret-set github githubToken
                  updaemon secret-set byteshelf apiKey xyz789
                """;
        }
    }
}

