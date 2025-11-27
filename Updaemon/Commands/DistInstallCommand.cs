using Updaemon.Common.Models;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'dist-install' command to install a distribution service plugin.
    /// </summary>
    public class DistInstallCommand
    {
        private readonly IConfigManager _configManager;
        private readonly HttpClient _httpClient;
        private readonly IOutputWriter _outputWriter;
        private readonly IDistributionServiceClient _distributionClient;
        private readonly string _pluginsDirectory;

        public DistInstallCommand(
            IConfigManager configManager,
            HttpClient httpClient,
            IOutputWriter outputWriter,
            IDistributionServiceClient distributionClient,
            IPluginUrlResolver pluginUrlResolver)
        {
            _configManager = configManager;
            _httpClient = httpClient;
            _outputWriter = outputWriter;
            _distributionClient = distributionClient;
            _pluginsDirectory = "/var/lib/updaemon/plugins";
        }

        public DistInstallCommand(
            IConfigManager configManager,
            HttpClient httpClient,
            IOutputWriter outputWriter,
            IDistributionServiceClient distributionClient,
            IPluginUrlResolver pluginUrlResolver,
            string pluginsDirectory)
        {
            _configManager = configManager;
            _httpClient = httpClient;
            _outputWriter = outputWriter;
            _distributionClient = distributionClient;
            _pluginsDirectory = pluginsDirectory;
        }

        public async Task ExecuteAsync(string? alias, string url, CancellationToken cancellationToken = default)
        {
            // If alias is explicitly provided, check if it already exists before downloading
            if (alias != null)
            {
                InstalledPluginInfo? existingPlugin = await _configManager.GetPluginAsync(alias, cancellationToken);
                if (existingPlugin != null)
                {
                    throw new InvalidOperationException($"Plugin with alias '{alias}' is already installed. Use a different alias or remove the existing plugin first.");
                }
            }

            _outputWriter.WriteLine($"Downloading distribution plugin from: {url}");

            // Download the plugin
            byte[] pluginData = await _httpClient.GetByteArrayAsync(url, cancellationToken);
            _outputWriter.WriteLine($"Downloaded {pluginData.Length} bytes");

            // Determine filename from URL
            string filename = Path.GetFileName(new Uri(url).LocalPath);
            if (string.IsNullOrEmpty(filename))
            {
                filename = "distribution-plugin";
            }

            // Create plugins directory
            Directory.CreateDirectory(_pluginsDirectory);

            // Temporarily save the plugin to get metadata
            string tempPluginPath = Path.Combine(_pluginsDirectory, $"temp_{Guid.NewGuid():N}_{filename}");
            await File.WriteAllBytesAsync(tempPluginPath, pluginData, cancellationToken);

            // Make it executable (on Linux)
            try
            {
                System.Diagnostics.Process.Start("chmod", $"+x {tempPluginPath}")?.WaitForExit();
            }
            catch
            {
                _outputWriter.WriteLine("Warning: Could not make plugin executable. You may need to run 'chmod +x' manually.");
            }

            // Get plugin metadata
            await _distributionClient.ConnectAsync(tempPluginPath, cancellationToken);
            DistributionServiceInformation serviceInfo = await _distributionClient.GetServiceInformationAsync(cancellationToken);
            await _distributionClient.DisposeAsync();

            // Determine alias
            string finalAlias = alias ?? serviceInfo.DefaultAlias;
            if (string.IsNullOrEmpty(finalAlias))
            {
                throw new InvalidOperationException("Plugin does not provide a default alias and none was specified. Use --as to specify an alias.");
            }

            // Check if alias already exists
            InstalledPluginInfo? existing = await _configManager.GetPluginAsync(finalAlias, cancellationToken);
            if (existing != null)
            {
                throw new InvalidOperationException($"Plugin with alias '{finalAlias}' is already installed. Use a different alias or remove the existing plugin first.");
            }

            // Create plugin-specific directory
            string pluginDirectory = Path.Combine(_pluginsDirectory, finalAlias);
            Directory.CreateDirectory(pluginDirectory);

            // Move plugin to final location
            string finalPluginPath = Path.Combine(pluginDirectory, filename);
            File.Move(tempPluginPath, finalPluginPath, overwrite: true);
            _outputWriter.WriteLine($"Saved plugin to: {finalPluginPath}");

            // Register plugin
            InstalledPluginInfo pluginInfo = new InstalledPluginInfo
            {
                Alias = finalAlias,
                Path = finalPluginPath
            };
            await _configManager.AddOrUpdatePluginAsync(pluginInfo, cancellationToken);
            _outputWriter.WriteLine($"Distribution plugin '{finalAlias}' installed successfully");
            _outputWriter.WriteLine($"  Name: {serviceInfo.FullName}");
            _outputWriter.WriteLine($"  Version: {serviceInfo.Version}");
            _outputWriter.WriteLine($"  Description: {serviceInfo.Description}");
        }
    }
}

