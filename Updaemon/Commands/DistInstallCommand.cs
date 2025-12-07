using Updaemon.Common.Models;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'dist-install' command to install a distribution service plugin.
    /// </summary>
    public class DistInstallCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly HttpClient _httpClient;
        private readonly IOutputWriter _outputWriter;
        private readonly IDistributionServiceClient _distributionClient;
        private readonly IPluginUrlResolver _pluginUrlResolver;
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
            _pluginUrlResolver = pluginUrlResolver;
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
            _pluginUrlResolver = pluginUrlResolver;
            _pluginsDirectory = pluginsDirectory;
        }

        public string Name => "dist-install";

        public string Description => "Install a distribution service plugin";

        public string Usage => "updaemon dist-install [--as <alias>] <plugin-name|url>";

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, _outputWriter);

            // Parse --as flag
            string? alias = parser.GetFlag("--as");
            string? urlOrName = parser.GetFirstNonFlag();

            if (string.IsNullOrEmpty(urlOrName))
            {
                _outputWriter.WriteError("Error: 'dist-install' command requires a plugin name or URL");
                _outputWriter.WriteLine($"Usage: {Usage}");
                return 1;
            }

            // Determine if input is a URL or a plugin name
            string finalUrl = urlOrName;
            string? finalAlias = alias;
            if (!urlOrName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !urlOrName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // It's a plugin name, resolve it to a URL
                try
                {
                    finalUrl = await _pluginUrlResolver.ResolveAsync(urlOrName, cancellationToken);
                    // If no explicit alias was provided, use the plugin name as the alias
                    if (finalAlias == null)
                    {
                        finalAlias = urlOrName;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Re-throw to preserve the helpful error message from the resolver
                    throw;
                }
            }

            // If alias is explicitly provided, check if it already exists before downloading
            if (finalAlias != null)
            {
                InstalledPluginInfo? existingPlugin = await _configManager.GetPluginAsync(finalAlias, cancellationToken);
                if (existingPlugin != null)
                {
                    _outputWriter.WriteError($"Plugin with alias '{finalAlias}' is already installed. Use a different alias or remove the existing plugin first.");
                    return 1;
                }
            }

            _outputWriter.WriteLine($"Downloading distribution plugin from: {finalUrl}");

            // Download the plugin
            byte[] pluginData = await _httpClient.GetByteArrayAsync(finalUrl, cancellationToken);
            _outputWriter.WriteLine($"Downloaded {pluginData.Length} bytes");

            // Determine filename from URL
            string filename = Path.GetFileName(new Uri(finalUrl).LocalPath);
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

            // Determine alias (if not already set from plugin name resolution)
            if (finalAlias == null)
            {
                finalAlias = serviceInfo.DefaultAlias;
            }

            if (string.IsNullOrEmpty(finalAlias))
            {
                _outputWriter.WriteError("Plugin does not provide a default alias and none was specified. Use --as to specify an alias.");
                return 1;
            }

            // Check if alias already exists (double-check in case it was set from plugin name)
            InstalledPluginInfo? existing = await _configManager.GetPluginAsync(finalAlias, cancellationToken);
            if (existing != null)
            {
                _outputWriter.WriteError($"Plugin with alias '{finalAlias}' is already installed. Use a different alias or remove the existing plugin first.");
                return 1;
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
            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                Dist-Install Command

                Usage:
                  updaemon dist-install [--as <alias>] <plugin-name|url>

                Description:
                  Installs a distribution service plugin. The plugin can be specified as a URL
                  or as a plugin name (e.g., "github") which will be resolved to a URL. If
                  --as is not specified and a plugin name is used, the plugin name becomes the
                  alias. If a URL is used without --as, the plugin's default alias is used.

                Examples:
                  updaemon dist-install github
                  updaemon dist-install --as github https://example.com/plugin
                  updaemon dist-install https://example.com/plugin
                """;
        }
    }
}

