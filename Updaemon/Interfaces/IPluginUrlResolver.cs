namespace Updaemon.Interfaces
{
    /// <summary>
    /// Resolves registry plugin names to the download URL of their build for this machine.
    /// </summary>
    public interface IPluginUrlResolver
    {
        /// <summary>
        /// The names of the plugins in the registry.
        /// </summary>
        IReadOnlyCollection<string> PluginNames { get; }

        /// <summary>
        /// Returns the URL of a registry plugin's build for this machine in the given updaemon
        /// release, or null when the name is not in the registry.
        /// </summary>
        /// <param name="pluginName">The plugin's registry name, e.g. "github".</param>
        /// <param name="updaemonVersion">The updaemon release the plugin comes from: the running version, or the one self-update installs.</param>
        string? Resolve(string pluginName, Version updaemonVersion);
    }
}
