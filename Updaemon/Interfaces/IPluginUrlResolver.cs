namespace Updaemon.Interfaces
{
    /// <summary>
    /// Resolves plugin names to download URLs.
    /// </summary>
    public interface IPluginUrlResolver
    {
        /// <summary>
        /// Resolves a plugin name to its download URL.
        /// </summary>
        /// <param name="pluginName">The name of the plugin to resolve.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The download URL for the plugin.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the plugin name is not found in the registry or when the registry cannot be fetched.</exception>
        Task<string> ResolveAsync(string pluginName, CancellationToken cancellationToken = default);
    }
}

