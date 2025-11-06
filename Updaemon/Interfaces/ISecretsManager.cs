namespace Updaemon.Interfaces
{
    /// <summary>
    /// Manages secrets stored per-plugin in /var/lib/updaemon/plugins/&lt;alias&gt;/secrets.txt in key=value format.
    /// </summary>
    public interface ISecretsManager
    {
        /// <summary>
        /// Sets or updates a secret key-value pair for a specific plugin.
        /// </summary>
        Task SetSecretAsync(string pluginAlias, string key, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a secret value by key for a specific plugin.
        /// </summary>
        Task<string?> GetSecretAsync(string pluginAlias, string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all secrets for a specific plugin as a formatted string (key=value pairs separated by line breaks).
        /// Returns null if no secrets are configured for the plugin.
        /// </summary>
        Task<string?> GetPluginSecretsFormattedAsync(string pluginAlias, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a secret by key for a specific plugin.
        /// </summary>
        Task RemoveSecretAsync(string pluginAlias, string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the path to a plugin's secrets file.
        /// </summary>
        string GetPluginSecretsPath(string pluginAlias);
    }
}

