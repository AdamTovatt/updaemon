using Updaemon.Models;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Installs a distribution plugin: resolves its source, downloads it beside any installed
    /// copy, smoke-runs it by reading its service information, swaps it in and records it.
    /// </summary>
    public interface IPluginInstaller
    {
        /// <summary>
        /// Installs the plugin from <paramref name="source"/>, replacing a plugin installed under
        /// the same alias. A download or smoke run that fails leaves any installed copy as it was.
        /// </summary>
        /// <param name="source">Where to install the plugin from.</param>
        /// <param name="alias">The alias to install it under. Null means the registry name for a registry plugin, or the plugin's default alias for a URL.</param>
        /// <param name="updaemonVersion">The updaemon release a registry plugin comes from: the running version, or the one self-update installs.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="InvalidOperationException">The source names no registry plugin, or no alias can be found.</exception>
        /// <exception cref="PluginException">The downloaded plugin failed its smoke run, e.g. it speaks another plugin protocol.</exception>
        Task<PluginInstallResult> InstallAsync(PluginSource source, string? alias, Version updaemonVersion, CancellationToken cancellationToken = default);
    }
}
