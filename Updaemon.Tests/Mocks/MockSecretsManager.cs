using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of ISecretsManager with in-memory storage per plugin.
    /// </summary>
    public class MockSecretsManager : ISecretsManager
    {
        private readonly Dictionary<string, Dictionary<string, string>> _pluginSecrets = new Dictionary<string, Dictionary<string, string>>();
        public List<string> MethodCalls { get; } = new List<string>();

        public string GetPluginSecretsPath(string pluginAlias)
        {
            return $"/var/lib/updaemon/plugins/{pluginAlias}/secrets.txt";
        }

        public Task SetSecretAsync(string pluginAlias, string key, string value, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(SetSecretAsync)}:{pluginAlias}:{key}:{value}");
            if (!_pluginSecrets.ContainsKey(pluginAlias))
            {
                _pluginSecrets[pluginAlias] = new Dictionary<string, string>();
            }
            _pluginSecrets[pluginAlias][key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> GetSecretAsync(string pluginAlias, string key, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(GetSecretAsync)}:{pluginAlias}:{key}");
            if (_pluginSecrets.TryGetValue(pluginAlias, out Dictionary<string, string>? secrets))
            {
                return Task.FromResult<string?>(secrets.GetValueOrDefault(key));
            }
            return Task.FromResult<string?>(null);
        }

        public Task<string?> GetPluginSecretsFormattedAsync(string pluginAlias, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(GetPluginSecretsFormattedAsync)}:{pluginAlias}");

            if (!_pluginSecrets.TryGetValue(pluginAlias, out Dictionary<string, string>? secrets) || secrets.Count == 0)
            {
                return Task.FromResult<string?>(null);
            }

            string formatted = string.Join(Environment.NewLine, secrets.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            return Task.FromResult<string?>(formatted);
        }

        public Task RemoveSecretAsync(string pluginAlias, string key, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(RemoveSecretAsync)}:{pluginAlias}:{key}");
            if (_pluginSecrets.TryGetValue(pluginAlias, out Dictionary<string, string>? secrets))
            {
                secrets.Remove(key);
            }
            return Task.CompletedTask;
        }
    }
}

