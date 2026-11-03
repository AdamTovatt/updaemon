using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// The only way a command gets a plugin to run. A registry plugin always comes from the running
    /// updaemon's release: one that is missing, or from another release (after a self-update), is
    /// installed from it first. A URL plugin is used as installed.
    /// </summary>
    public class PluginProvider
    {
        private readonly IConfigManager _configManager;
        private readonly IPluginInstaller _pluginInstaller;
        private readonly IPluginUrlResolver _pluginUrlResolver;
        private readonly UpdaemonVersion _updaemonVersion;
        private readonly IOutputWriter _outputWriter;

        public PluginProvider(
            IConfigManager configManager,
            IPluginInstaller pluginInstaller,
            IPluginUrlResolver pluginUrlResolver,
            UpdaemonVersion updaemonVersion,
            IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _pluginInstaller = pluginInstaller;
            _pluginUrlResolver = pluginUrlResolver;
            _updaemonVersion = updaemonVersion;
            _outputWriter = outputWriter;
        }

        /// <exception cref="PluginException">
        /// The plugin is not installed and not in the registry, or installing it from the running
        /// release failed (an installed copy is then left as it was); the message says what to do.
        /// </exception>
        public async Task<InstalledPluginInfo> GetOrInstallAsync(string alias, CancellationToken cancellationToken = default)
        {
            InstalledPluginInfo? plugin = await _configManager.GetPluginAsync(alias, cancellationToken);
            if (plugin != null && !plugin.Source.IsOutOfLineWith(_updaemonVersion))
            {
                return plugin;
            }

            if (plugin == null && !_pluginUrlResolver.PluginNames.Contains(alias))
            {
                throw new PluginException(
                    $"the plugin '{alias}' is not installed and is not in the plugin registry ({string.Join(", ", _pluginUrlResolver.PluginNames)}). " +
                    $"Install it with 'updaemon dist-install --as {alias} <url>'.");
            }

            return await InstallAsync(alias, replacing: plugin, cancellationToken);
        }

        /// <summary>Installs registry plugin <paramref name="alias"/> from the running release, over <paramref name="replacing"/> if given.</summary>
        private async Task<InstalledPluginInfo> InstallAsync(string alias, InstalledPluginInfo? replacing, CancellationToken cancellationToken)
        {
            string name = (replacing?.Source as PluginSource.Registry)?.Name ?? alias;
            _outputWriter.WriteLine(replacing == null
                ? $"Installing the '{alias}' plugin..."
                : $"Updating the '{alias}' plugin to updaemon {_updaemonVersion}'s build...");
            try
            {
                return (await _pluginInstaller.InstallAsync(new PluginSource.Registry(name, _updaemonVersion), alias, cancellationToken)).Plugin;
            }
            catch (Exception ex) when (ex is PluginException or InvalidOperationException or HttpRequestException)
            {
                throw new PluginException($"{(replacing == null ? "installing" : "updating")} the '{alias}' plugin failed: {ex.Message}");
            }
        }
    }
}
