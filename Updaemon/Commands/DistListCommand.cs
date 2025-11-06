using Updaemon.Common.Models;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'dist-list' command to list installed distribution plugins.
    /// </summary>
    public class DistListCommand
    {
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;
        private readonly IDistributionServiceClient _distributionClient;

        public DistListCommand(IConfigManager configManager, IOutputWriter outputWriter, IDistributionServiceClient distributionClient)
        {
            _configManager = configManager;
            _outputWriter = outputWriter;
            _distributionClient = distributionClient;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, InstalledPluginInfo> plugins = await _configManager.GetAllPluginsAsync(cancellationToken);

            if (plugins.Count == 0)
            {
                _outputWriter.WriteLine("No distribution plugins installed.");
                _outputWriter.WriteLine("Use 'updaemon dist-install <url>' to install a plugin.");
                return;
            }

            _outputWriter.WriteLine($"Installed distribution plugins ({plugins.Count}):");
            _outputWriter.WriteLine("");

            foreach (KeyValuePair<string, InstalledPluginInfo> plugin in plugins)
            {
                try
                {
                    if (!File.Exists(plugin.Value.Path))
                    {
                        _outputWriter.WriteLine($"  Alias: {plugin.Value.Alias}");
                        _outputWriter.WriteLine($"  Path: {plugin.Value.Path}");
                        _outputWriter.WriteLine($"  Status: Plugin file not found");
                        _outputWriter.WriteLine("");
                        continue;
                    }

                    // Get metadata from plugin
                    await _distributionClient.ConnectAsync(plugin.Value.Path, cancellationToken);
                    DistributionServiceInformation serviceInfo = await _distributionClient.GetServiceInformationAsync(cancellationToken);
                    await _distributionClient.DisposeAsync();

                    _outputWriter.WriteLine($"  Alias: {plugin.Value.Alias}");
                    _outputWriter.WriteLine($"  Name: {serviceInfo.FullName}");
                    _outputWriter.WriteLine($"  Version: {serviceInfo.Version}");
                    _outputWriter.WriteLine($"  Description: {serviceInfo.Description}");
                    _outputWriter.WriteLine($"  Path: {plugin.Value.Path}");

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
                    _outputWriter.WriteLine($"  Alias: {plugin.Value.Alias}");
                    _outputWriter.WriteLine($"  Path: {plugin.Value.Path}");
                    _outputWriter.WriteLine($"  Status: Error retrieving metadata - {ex.Message}");
                    _outputWriter.WriteLine("");
                }
            }
        }
    }
}

