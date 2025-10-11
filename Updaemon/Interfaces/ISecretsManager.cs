namespace Updaemon.Interfaces
{
    /// <summary>
    /// Manages secrets stored in /var/lib/updaemon/secrets.txt in key=value format.
    /// </summary>
    public interface ISecretsManager
    {
        /// <summary>
        /// Sets or updates a secret key-value pair.
        /// </summary>
        Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a secret value by key.
        /// </summary>
        Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all secrets as a formatted string (key=value pairs separated by line breaks).
        /// Returns null if no secrets are configured.
        /// </summary>
        Task<string?> GetAllSecretsFormattedAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a secret by key.
        /// </summary>
        Task RemoveSecretAsync(string key, CancellationToken cancellationToken = default);
    }
}

