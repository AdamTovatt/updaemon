using Updaemon.Common.Models;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'dist-update' command to update installed distribution plugins.
    /// </summary>
    public class DistUpdateCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;
        private readonly IPluginInstaller _pluginInstaller;
        private readonly IPluginDownloader _pluginDownloader;
        private readonly Version _updaemonVersion;

        /// <param name="pluginDownloader">Reads the installed plugin's version.</param>
        /// <param name="updaemonVersion">The running updaemon's version: registry plugins come from its release.</param>
        public DistUpdateCommand(
            IConfigManager configManager,
            IOutputWriter outputWriter,
            IPluginInstaller pluginInstaller,
            IPluginDownloader pluginDownloader,
            UpdaemonVersion updaemonVersion)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
            _pluginInstaller = pluginInstaller;
            _pluginDownloader = pluginDownloader;
            _updaemonVersion = updaemonVersion.Value;
        }

        public string Name => "dist-update";

        public string Description => "Update installed distribution plugins";

        public string Usage => "updaemon dist-update [alias]";

        public bool ChangesState => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            string? specificAlias = args.Length > 0 ? args[0] : null;

            IReadOnlyDictionary<string, InstalledPluginInfo> plugins = await _configManager.GetAllPluginsAsync(cancellationToken);

            List<InstalledPluginInfo> pluginsToUpdate;
            if (specificAlias != null)
            {
                if (!plugins.TryGetValue(specificAlias, out InstalledPluginInfo? plugin))
                {
                    _outputWriter.WriteError($"Error: Plugin '{specificAlias}' is not installed.");
                    return 1;
                }

                pluginsToUpdate = new List<InstalledPluginInfo> { plugin };
            }
            else if (plugins.Count == 0)
            {
                _outputWriter.WriteLine("No distribution plugins installed.");
                return 0;
            }
            else
            {
                pluginsToUpdate = new List<InstalledPluginInfo>(plugins.Values);
            }

            int updatedCount = 0;
            int failedCount = 0;
            foreach (InstalledPluginInfo plugin in pluginsToUpdate)
            {
                try
                {
                    bool updated = await UpdatePluginAsync(plugin, cancellationToken);
                    if (updated)
                    {
                        updatedCount++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _outputWriter.WriteError($"Failed to update '{plugin.Alias}': {ex.Message}");
                }
            }

            if (updatedCount == 0 && failedCount == 0)
            {
                _outputWriter.WriteLine("All plugins are up to date.");
            }

            return 0;
        }

        private async Task<bool> UpdatePluginAsync(InstalledPluginInfo plugin, CancellationToken cancellationToken)
        {
            _outputWriter.WriteLine($"\nChecking plugin '{plugin.Alias}' for updates...");

            if (plugin.Source is not PluginSource.Registry)
            {
                _outputWriter.WriteLine($"Skipping '{plugin.Alias}': it was installed from a URL. Reinstall it with 'updaemon dist-install --as {plugin.Alias} <url>'.");
                return false;
            }

            string? installedVersion = await ReadInstalledVersionAsync(plugin, cancellationToken);

            // A registry plugin follows the running updaemon's release, so the install replaces
            // it with that release's build whatever its version.
            PluginInstallResult result = await _pluginInstaller.InstallAsync(plugin.Source, plugin.Alias, _updaemonVersion, cancellationToken);
            string availableVersion = result.ServiceInformation.Version;
            if (availableVersion == installedVersion)
            {
                _outputWriter.WriteLine($"Plugin '{plugin.Alias}' is already up to date.");
                return false;
            }

            _outputWriter.WriteLine($"Updated '{plugin.Alias}' to version {availableVersion}");
            return true;
        }

        private async Task<string?> ReadInstalledVersionAsync(InstalledPluginInfo plugin, CancellationToken cancellationToken)
        {
            if (!File.Exists(plugin.Path))
            {
                _outputWriter.WriteLine($"Plugin binary not found at '{plugin.Path}'. Will download latest.");
                return null;
            }

            try
            {
                DistributionServiceInformation installedInfo = await _pluginDownloader.InspectLocalAsync(plugin.Path, cancellationToken);
                _outputWriter.WriteLine($"Installed version: {installedInfo.Version}");
                return installedInfo.Version;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _outputWriter.WriteLine($"Warning: Could not read installed plugin version ({ex.Message}). Will download latest.");
                return null;
            }
        }

        public string GetDetailedHelp()
        {
            return """
                Dist-Update Command

                Usage:
                  updaemon dist-update [alias]

                Description:
                  Updates installed registry plugins to the build in the running updaemon's
                  release. If an alias is provided, only that plugin is updated.
                  Otherwise, all installed plugins are checked.

                  Plugins installed from a URL are skipped with a message; reinstall them
                  with dist-install.

                Examples:
                  updaemon dist-update           # Update all plugins
                  updaemon dist-update github    # Update only the github plugin
                """;
        }
    }
}
