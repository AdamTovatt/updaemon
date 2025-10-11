using Updaemon.Interfaces;

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
        private readonly string _pluginsDirectory;

        public DistInstallCommand(
            IConfigManager configManager,
            HttpClient httpClient,
            IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _httpClient = httpClient;
            _outputWriter = outputWriter;
            _pluginsDirectory = "/var/lib/updaemon/plugins";
        }

        public DistInstallCommand(
            IConfigManager configManager,
            HttpClient httpClient,
            IOutputWriter outputWriter,
            string pluginsDirectory)
        {
            _configManager = configManager;
            _httpClient = httpClient;
            _outputWriter = outputWriter;
            _pluginsDirectory = pluginsDirectory;
        }

        public async Task ExecuteAsync(string url, CancellationToken cancellationToken = default)
        {
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

            // Save the plugin
            string pluginPath = Path.Combine(_pluginsDirectory, filename);
            await File.WriteAllBytesAsync(pluginPath, pluginData, cancellationToken);
            _outputWriter.WriteLine($"Saved plugin to: {pluginPath}");

            // Make it executable (on Linux)
            try
            {
                System.Diagnostics.Process.Start("chmod", $"+x {pluginPath}")?.WaitForExit();
                _outputWriter.WriteLine("Made plugin executable");
            }
            catch
            {
                _outputWriter.WriteLine("Warning: Could not make plugin executable. You may need to run 'chmod +x' manually.");
            }

            // Update config
            await _configManager.SetDistributionPluginPathAsync(pluginPath, cancellationToken);
            _outputWriter.WriteLine("Distribution plugin installed successfully");
        }
    }
}

