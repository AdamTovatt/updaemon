namespace Updaemon.Interfaces
{
    /// <summary>
    /// Stores plugin and app secrets, readable by the scope owner only.
    /// </summary>
    public interface ISecretsManager
    {
        Task SetSecretAsync(string pluginAlias, string key, string value, CancellationToken cancellationToken = default);

        Task<string?> GetSecretAsync(string pluginAlias, string key, CancellationToken cancellationToken = default);

        /// <summary>The plugin's secrets as key=value lines, or null when it has none.</summary>
        Task<string?> GetPluginSecretsFormattedAsync(string pluginAlias, CancellationToken cancellationToken = default);

        Task RemoveSecretAsync(string pluginAlias, string key, CancellationToken cancellationToken = default);

        string GetPluginSecretsPath(string pluginAlias);

        /// <summary>The app's secret settings; empty when it has none.</summary>
        Task<Dictionary<string, string>> GetAppSecretsAsync(string appName, CancellationToken cancellationToken = default);

        /// <summary>Replaces the app's secret settings. An empty set deletes the app's secrets file.</summary>
        Task SaveAppSecretsAsync(string appName, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken = default);

        void DeleteAppSecrets(string appName);
    }
}
