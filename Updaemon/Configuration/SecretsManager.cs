using Updaemon.Interfaces;

namespace Updaemon.Configuration
{
    /// <summary>
    /// Manages secrets stored per-plugin in /var/lib/updaemon/plugins/&lt;alias&gt;/secrets.txt in key=value format.
    /// </summary>
    public class SecretsManager : ISecretsManager
    {
        private const string ConfigDirectory = "/var/lib/updaemon";
        private const string PluginsDirectory = "plugins";
        private const string SecretsFileName = "secrets.txt";

        private readonly string _configDirectory;
        private readonly string _pluginsDirectory;

        public SecretsManager()
        {
            _configDirectory = ConfigDirectory;
            _pluginsDirectory = Path.Combine(_configDirectory, PluginsDirectory);
        }

        public SecretsManager(string configDirectory)
        {
            _configDirectory = configDirectory;
            _pluginsDirectory = Path.Combine(_configDirectory, PluginsDirectory);
        }

        public string GetPluginSecretsPath(string pluginAlias)
        {
            string pluginDirectory = Path.Combine(_pluginsDirectory, pluginAlias);
            return Path.Combine(pluginDirectory, SecretsFileName);
        }

        public async Task SetSecretAsync(string pluginAlias, string key, string value, CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> secrets = await LoadPluginSecretsAsync(pluginAlias, cancellationToken);
            secrets[key] = value;
            await SavePluginSecretsAsync(pluginAlias, secrets, cancellationToken);
        }

        public async Task<string?> GetSecretAsync(string pluginAlias, string key, CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> secrets = await LoadPluginSecretsAsync(pluginAlias, cancellationToken);
            return secrets.GetValueOrDefault(key);
        }

        public async Task<string?> GetPluginSecretsFormattedAsync(string pluginAlias, CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> secrets = await LoadPluginSecretsAsync(pluginAlias, cancellationToken);

            if (secrets.Count == 0)
            {
                return null;
            }

            return string.Join(Environment.NewLine, secrets.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        public async Task RemoveSecretAsync(string pluginAlias, string key, CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> secrets = await LoadPluginSecretsAsync(pluginAlias, cancellationToken);
            secrets.Remove(key);
            await SavePluginSecretsAsync(pluginAlias, secrets, cancellationToken);
        }

        private async Task<Dictionary<string, string>> LoadPluginSecretsAsync(string pluginAlias, CancellationToken cancellationToken = default)
        {
            string secretsFilePath = GetPluginSecretsPath(pluginAlias);

            if (!File.Exists(secretsFilePath))
            {
                return new Dictionary<string, string>();
            }

            string content = await File.ReadAllTextAsync(secretsFilePath, cancellationToken);
            Dictionary<string, string> secrets = new Dictionary<string, string>();

            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                int separatorIndex = line.IndexOf('=');
                if (separatorIndex > 0)
                {
                    string key = line.Substring(0, separatorIndex);
                    string value = line.Substring(separatorIndex + 1);
                    secrets[key] = value;
                }
            }

            return secrets;
        }

        private async Task SavePluginSecretsAsync(string pluginAlias, Dictionary<string, string> secrets, CancellationToken cancellationToken = default)
        {
            string pluginDirectory = Path.Combine(_pluginsDirectory, pluginAlias);
            Directory.CreateDirectory(pluginDirectory);

            string secretsFilePath = GetPluginSecretsPath(pluginAlias);
            string content = string.Join(Environment.NewLine, secrets.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            await File.WriteAllTextAsync(secretsFilePath, content, cancellationToken);
        }
    }
}

