using Updaemon.Common.Models;
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
        private readonly IPluginUrlResolver _pluginUrlResolver;
        private readonly IPluginDownloader _pluginDownloader;

        public DistUpdateCommand(
            IConfigManager configManager,
            IOutputWriter outputWriter,
            IPluginUrlResolver pluginUrlResolver,
            IPluginDownloader pluginDownloader)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
            _pluginUrlResolver = pluginUrlResolver;
            _pluginDownloader = pluginDownloader;
        }

        public string Name => "dist-update";

        public string Description => "Update installed distribution plugins";

        public string Usage => "updaemon dist-update [alias]";

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

            // Resolve download URL from registry
            string downloadUrl;
            try
            {
                downloadUrl = await _pluginUrlResolver.ResolveAsync(plugin.Alias, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                _outputWriter.WriteLine($"Skipping '{plugin.Alias}': not found in plugin registry. Update manually using a direct URL.");
                return false;
            }

            // Get current version from installed plugin
            string? currentVersionString = null;
            Version? currentVersion = null;
            if (File.Exists(plugin.Path))
            {
                try
                {
                    DistributionServiceInformation currentInfo = await _pluginDownloader.InspectLocalAsync(plugin.Path, cancellationToken);

                    currentVersionString = currentInfo.Version;
                    Version.TryParse(currentInfo.Version, out currentVersion);
                    _outputWriter.WriteLine($"Installed version: {currentInfo.Version}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    _outputWriter.WriteLine("Warning: Could not read installed plugin version. Will download latest.");
                }
            }
            else
            {
                _outputWriter.WriteLine($"Plugin binary not found at '{plugin.Path}'. Will download latest.");
            }

            // Download and inspect new plugin
            string pluginDirectory = Path.GetDirectoryName(plugin.Path)!;
            _outputWriter.WriteLine($"Downloading from: {downloadUrl}");
            PluginDownloadResult downloadResult = await _pluginDownloader.DownloadAndInspectAsync(downloadUrl, pluginDirectory, cancellationToken);
            try
            {
                DistributionServiceInformation newInfo = downloadResult.ServiceInformation;
                Version.TryParse(newInfo.Version, out Version? newVersion);
                _outputWriter.WriteLine($"Available version: {newInfo.Version}");

                // Compare versions
                if (currentVersion != null && newVersion != null && newVersion <= currentVersion)
                {
                    _outputWriter.WriteLine($"Plugin '{plugin.Alias}' is already up to date.");
                    return false;
                }

                // Fallback: compare raw version strings when parsing fails
                if (currentVersionString != null && currentVersionString == newInfo.Version)
                {
                    _outputWriter.WriteLine($"Plugin '{plugin.Alias}' is already up to date.");
                    return false;
                }

                // Replace binary
                File.Move(downloadResult.TempFilePath, plugin.Path, overwrite: true);
                _outputWriter.WriteLine($"Updated '{plugin.Alias}' to version {newInfo.Version}");
                return true;
            }
            finally
            {
                try
                {
                    if (File.Exists(downloadResult.TempFilePath))
                        File.Delete(downloadResult.TempFilePath);
                }
                catch { }
            }
        }

        public string GetDetailedHelp()
        {
            return """
                Dist-Update Command

                Usage:
                  updaemon dist-update [alias]

                Description:
                  Updates installed distribution plugins to the latest version from the
                  plugin registry. If an alias is provided, only that plugin is updated.
                  Otherwise, all installed plugins are checked.

                  Plugins that were installed via direct URL and are not in the registry
                  will be skipped with a message.

                Examples:
                  updaemon dist-update           # Update all plugins
                  updaemon dist-update github    # Update only the github plugin
                """;
        }
    }
}
