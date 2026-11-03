using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// The plugin apps are downloaded with: the installed one, else the registry's, installed now from
    /// the running updaemon's release. One home for that rule, for install and update.
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
        /// The plugin is not installed and not in the registry, or installing it failed; the message says what to do.
        /// </exception>
        public async Task<InstalledPluginInfo> GetOrInstallAsync(string alias, CancellationToken cancellationToken = default)
        {
            InstalledPluginInfo? plugin = await _configManager.GetPluginAsync(alias, cancellationToken);
            if (plugin != null)
            {
                return plugin;
            }

            if (!_pluginUrlResolver.PluginNames.Contains(alias))
            {
                throw new PluginException(
                    $"the plugin '{alias}' is not installed and is not in the plugin registry ({string.Join(", ", _pluginUrlResolver.PluginNames)}). " +
                    $"Install it with 'updaemon dist-install <url> --as {alias}'.");
            }

            try
            {
                _outputWriter.WriteLine($"Installing the '{alias}' plugin...");
                return (await _pluginInstaller.InstallAsync(new PluginSource.Registry(alias), alias, _updaemonVersion.Value, cancellationToken)).Plugin;
            }
            catch (Exception ex) when (ex is PluginException or InvalidOperationException or HttpRequestException)
            {
                throw new PluginException($"installing the '{alias}' plugin failed: {ex.Message}");
            }
        }
    }
}
