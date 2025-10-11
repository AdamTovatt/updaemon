using Updaemon.Interfaces;

namespace Updaemon.Configuration
{
    /// <summary>
    /// Manages secrets stored in /var/lib/updaemon/secrets.txt in key=value format.
    /// </summary>
    public class SecretsManager : ISecretsManager
    {
        private const string ConfigDirectory = "/var/lib/updaemon";
        private const string SecretsFileName = "secrets.txt";

        private readonly string _secretsFilePath;
        private readonly string _configDirectory;

        public SecretsManager()
        {
            _configDirectory = ConfigDirectory;
            _secretsFilePath = Path.Combine(_configDirectory, SecretsFileName);
        }

        public SecretsManager(string configDirectory)
        {
            _configDirectory = configDirectory;
            _secretsFilePath = Path.Combine(_configDirectory, SecretsFileName);
        }

        public async Task SetSecretAsync(string key, string value)
        {
            Dictionary<string, string> secrets = await LoadSecretsAsync();
            secrets[key] = value;
            await SaveSecretsAsync(secrets);
        }

        public async Task<string?> GetSecretAsync(string key)
        {
            Dictionary<string, string> secrets = await LoadSecretsAsync();
            return secrets.GetValueOrDefault(key);
        }

        public async Task<string?> GetAllSecretsFormattedAsync()
        {
            Dictionary<string, string> secrets = await LoadSecretsAsync();

            if (secrets.Count == 0)
            {
                return null;
            }

            return string.Join(Environment.NewLine, secrets.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        public async Task RemoveSecretAsync(string key)
        {
            Dictionary<string, string> secrets = await LoadSecretsAsync();
            secrets.Remove(key);
            await SaveSecretsAsync(secrets);
        }

        private async Task<Dictionary<string, string>> LoadSecretsAsync()
        {
            if (!File.Exists(_secretsFilePath))
            {
                return new Dictionary<string, string>();
            }

            string content = await File.ReadAllTextAsync(_secretsFilePath);
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

        private async Task SaveSecretsAsync(Dictionary<string, string> secrets)
        {
            Directory.CreateDirectory(_configDirectory);
            string content = string.Join(Environment.NewLine, secrets.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            await File.WriteAllTextAsync(_secretsFilePath, content);
        }
    }
}

