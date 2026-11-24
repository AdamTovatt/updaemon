using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>Real secrets, with a call around each save of an app's.</summary>
    public sealed class WatchedSecretsManager : ISecretsManager
    {
        private readonly ISecretsManager _inner;

        public WatchedSecretsManager(ISecretsManager inner)
        {
            _inner = inner;
        }

        /// <summary>Called before each save of an app's secrets.</summary>
        public Action? OnSave { get; set; }

        /// <summary>Called after each save that worked.</summary>
        public Action? OnSaved { get; set; }

        public async Task SaveAppSecretsAsync(string appName, string? owner, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken = default)
        {
            OnSave?.Invoke();
            await _inner.SaveAppSecretsAsync(appName, owner, secrets, cancellationToken);
            OnSaved?.Invoke();
        }

        public Task SetSecretAsync(string pluginAlias, string key, string value, CancellationToken cancellationToken = default) => _inner.SetSecretAsync(pluginAlias, key, value, cancellationToken);

        public Task<string?> GetSecretAsync(string pluginAlias, string key, CancellationToken cancellationToken = default) => _inner.GetSecretAsync(pluginAlias, key, cancellationToken);

        public Task<string?> GetPluginSecretsFormattedAsync(string pluginAlias, CancellationToken cancellationToken = default) => _inner.GetPluginSecretsFormattedAsync(pluginAlias, cancellationToken);

        public Task RemoveSecretAsync(string pluginAlias, string key, CancellationToken cancellationToken = default) => _inner.RemoveSecretAsync(pluginAlias, key, cancellationToken);

        public Task<Dictionary<string, string>> GetAppSecretsAsync(string appName, CancellationToken cancellationToken = default) => _inner.GetAppSecretsAsync(appName, cancellationToken);

        public string GetPluginSecretsPath(string pluginAlias) => _inner.GetPluginSecretsPath(pluginAlias);

        public void DeleteAppSecrets(string appName) => _inner.DeleteAppSecrets(appName);
    }
}
