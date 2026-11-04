namespace Updaemon.Interfaces
{
    /// <summary>
    /// Stores plugin and app secrets in mode 0600 files: a plugin's owned by the scope's user, an
    /// app's by the user the app runs as.
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
        /// <param name="owner">The user the app runs as, who gets the file; null leaves it to the scope's user.</param>
        Task SaveAppSecretsAsync(string appName, string? owner, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken = default);

        void DeleteAppSecrets(string appName);
    }
}
