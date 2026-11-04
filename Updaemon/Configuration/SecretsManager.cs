using Updaemon.Interfaces;
using Updaemon.IO;

namespace Updaemon.Configuration
{
    /// <summary>
    /// Stores secrets, each file mode 0600: each plugin's in
    /// &lt;scope root&gt;/plugins/&lt;alias&gt;/secrets.txt (key=value lines, passed to the plugin as is),
    /// and each app's in &lt;scope root&gt;/secrets/&lt;app&gt;.env (an <see cref="EnvFile"/> that
    /// the app's wrappers load), owned by the user the app runs as. Who may read an app's secrets is
    /// decided here alone.
    /// </summary>
    public class SecretsManager : ISecretsManager
    {
        private const string SecretsFileName = "secrets.txt";

        private const UnixFileMode SecretsDirectoryMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

        private readonly UpdaemonScope _scope;
        private readonly IFilePermissionManager _filePermissionManager;

        public SecretsManager(UpdaemonScope scope, IFilePermissionManager filePermissionManager)
        {
            _scope = scope;
            _filePermissionManager = filePermissionManager;
        }

        public string GetPluginSecretsPath(string pluginAlias)
        {
            return Path.Combine(_scope.PluginsDirectory, pluginAlias, SecretsFileName);
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

        public Task<Dictionary<string, string>> GetAppSecretsAsync(string appName, CancellationToken cancellationToken = default)
        {
            return EnvFile.ReadAsync(_scope.GetSecretsFilePath(appName), cancellationToken);
        }

        public async Task SaveAppSecretsAsync(string appName, string? owner, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken = default)
        {
            if (secrets.Count == 0)
            {
                DeleteAppSecrets(appName);
                return;
            }

            if (!Directory.Exists(_scope.SecretsDirectory))
            {
                // 0711, whatever the umask: a service running as another user opens its own file by
                // name, but no one can list the directory.
                Directory.CreateDirectory(_scope.SecretsDirectory);
                File.SetUnixFileMode(_scope.SecretsDirectory, SecretsDirectoryMode);
            }

            // Given to its owner before it replaces the old file, so a service restarting meanwhile
            // never finds a file it cannot read.
            await EnvFile.WriteAsync(
                _scope.GetSecretsFilePath(appName),
                secrets,
                AtomicFile.OwnerOnly,
                newFile => owner == null ? Task.CompletedTask : _filePermissionManager.SetOwnerAsync(newFile, owner, cancellationToken),
                cancellationToken);
        }

        public void DeleteAppSecrets(string appName)
        {
            Files.DeleteIfExists(_scope.GetSecretsFilePath(appName));
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
            string content = string.Join(Environment.NewLine, secrets.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            await AtomicFile.WriteAsync(GetPluginSecretsPath(pluginAlias), content, AtomicFile.OwnerOnly, cancellationToken);
        }
    }
}
