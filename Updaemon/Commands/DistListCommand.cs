using Updaemon.Common.Models;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'dist-list' command to list installed distribution plugins.
    /// </summary>
    public class DistListCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;
        private readonly IDistributionServiceClient _distributionClient;
        private readonly UpdaemonVersion _updaemonVersion;

        public DistListCommand(IConfigManager configManager, IOutputWriter outputWriter, IDistributionServiceClient distributionClient, UpdaemonVersion updaemonVersion)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
            _distributionClient = distributionClient;
            _updaemonVersion = updaemonVersion;
        }

        public string Name => "dist-list";

        public string Description => "List installed distribution plugins";

        public string Usage => "updaemon dist-list";

        public bool ChangesState(string[] args) => false;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, InstalledPluginInfo> plugins = await _configManager.GetAllPluginsAsync(cancellationToken);
            bool selfUpdate = (await _configManager.LoadConfigAsync(cancellationToken)).SelfUpdate;

            if (plugins.Count == 0)
            {
                _outputWriter.WriteLine("No distribution plugins installed.");
                _outputWriter.WriteLine("Use 'updaemon dist-install <url>' to install a plugin.");
                return 0;
            }

            _outputWriter.WriteLine($"Installed distribution plugins ({plugins.Count}):");
            _outputWriter.WriteLine("");

            foreach (KeyValuePair<string, InstalledPluginInfo> plugin in plugins)
            {
                try
                {
                    // An out-of-line plugin is not run; the next install or update replaces it.
                    if (plugin.Value.Source is PluginSource.Registry registry && registry.IsOutOfLineWith(_updaemonVersion))
                    {
                        WriteUnreadable(plugin.Value, $"from updaemon {registry.Release}'s release; the next install or update replaces it with this version's build");
                        continue;
                    }

                    if (!File.Exists(plugin.Value.Path))
                    {
                        WriteUnreadable(plugin.Value, "Plugin file not found");
                        continue;
                    }

                    // Get metadata from plugin
                    DistributionServiceInformation serviceInfo = await _distributionClient.ConnectAsync(plugin.Value.Path, cancellationToken);
                    await _distributionClient.DisposeAsync();

                    _outputWriter.WriteLine($"  Alias: {plugin.Value.Alias}");
                    _outputWriter.WriteLine($"  Name: {serviceInfo.FullName}");
                    _outputWriter.WriteLine($"  Version: {serviceInfo.Version}");
                    _outputWriter.WriteLine($"  Description: {serviceInfo.Description}");
                    _outputWriter.WriteLine($"  Path: {plugin.Value.Path}");
                    _outputWriter.WriteLine($"  Updates: {UpdatesLabel(plugin.Value, selfUpdate)}");

                    if (serviceInfo.Secrets.Count > 0)
                    {
                        _outputWriter.WriteLine("  Secrets:");
                        foreach (DistributionSecretInfo secret in serviceInfo.Secrets)
                        {
                            string requiredLabel = secret.IsRequired ? "(required)" : "(optional)";
                            _outputWriter.WriteLine($"    - {secret.Name} {requiredLabel}");
                            if (!string.IsNullOrEmpty(secret.Description))
                            {
                                _outputWriter.WriteLine($"      {secret.Description}");
                            }
                        }
                    }

                    _outputWriter.WriteLine("");
                }
                catch (Exception ex)
                {
                    WriteUnreadable(plugin.Value, $"Error retrieving metadata - {ex.Message}");
                }
            }

            return 0;
        }

        private static string UpdatesLabel(InstalledPluginInfo plugin, bool selfUpdate) => plugin.Source switch
        {
            PluginSource.GitHub github => (selfUpdate
                    ? $"with the latest release of {github}, now {github.Tag}"
                    : $"not while self-update is off; now {github.Tag} of {github}")
                + (github.SkippedTag == null
                    ? string.Empty
                    : $"; skips {github.SkippedTag}, which was refused (retry it with 'updaemon dist-install --as {plugin.Alias} {github}')"),
            PluginSource.Url => "not updatable: installed from a URL, so only installing it again replaces it",
            _ => "with updaemon",
        };

        /// <summary>A plugin whose metadata is not shown, and why.</summary>
        private void WriteUnreadable(InstalledPluginInfo plugin, string status)
        {
            _outputWriter.WriteLine($"  Alias: {plugin.Alias}");
            _outputWriter.WriteLine($"  Path: {plugin.Path}");
            _outputWriter.WriteLine($"  Status: {status}");
            _outputWriter.WriteLine("");
        }

        public string GetDetailedHelp()
        {
            return """
                Dist-List Command

                Usage:
                  updaemon dist-list

                Description:
                  Lists all installed distribution service plugins with their metadata,
                  including name, version, description, and required secrets.

                Examples:
                  updaemon dist-list
                """;
        }
    }
}

