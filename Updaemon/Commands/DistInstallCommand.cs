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

        public DistInstallCommand(IConfigManager configManager, HttpClient httpClient)
        {
            _configManager = configManager;
            _httpClient = httpClient;
        }

        public async Task ExecuteAsync(string url)
        {
            Console.WriteLine($"Downloading distribution plugin from: {url}");

            // Download the plugin
            byte[] pluginData = await _httpClient.GetByteArrayAsync(url);
            Console.WriteLine($"Downloaded {pluginData.Length} bytes");

            // Determine filename from URL
            string filename = Path.GetFileName(new Uri(url).LocalPath);
            if (string.IsNullOrEmpty(filename))
            {
                filename = "distribution-plugin";
            }

            // Create plugins directory
            string pluginsDirectory = "/var/lib/updaemon/plugins";
            Directory.CreateDirectory(pluginsDirectory);

            // Save the plugin
            string pluginPath = Path.Combine(pluginsDirectory, filename);
            await File.WriteAllBytesAsync(pluginPath, pluginData);
            Console.WriteLine($"Saved plugin to: {pluginPath}");

            // Make it executable (on Linux)
            try
            {
                System.Diagnostics.Process.Start("chmod", $"+x {pluginPath}")?.WaitForExit();
                Console.WriteLine("Made plugin executable");
            }
            catch
            {
                Console.WriteLine("Warning: Could not make plugin executable. You may need to run 'chmod +x' manually.");
            }

            // Update config
            await _configManager.SetDistributionPluginPathAsync(pluginPath);
            Console.WriteLine("Distribution plugin installed successfully");
        }
    }
}

