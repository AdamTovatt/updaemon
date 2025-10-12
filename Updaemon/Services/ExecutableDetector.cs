using System.Text.Json;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Serialization;

namespace Updaemon.Services
{
    /// <summary>
    /// Detects executable files within service directories.
    /// </summary>
    public class ExecutableDetector : IExecutableDetector
    {
        public async Task<string?> FindExecutableAsync(string directoryPath, string serviceName, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                return null;
            }

            // First, check if there's an updaemon.json config file
            string configPath = Path.Combine(directoryPath, "updaemon.json");
            if (File.Exists(configPath))
            {
                string configJson = await File.ReadAllTextAsync(configPath, cancellationToken);
                
                AppConfig? config;
                try
                {
                    config = JsonSerializer.Deserialize(configJson, UpdaemonJsonContext.Default.AppConfig);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException($"Failed to parse updaemon.json in {directoryPath}: {ex.Message}", ex);
                }

                if (config?.ExecutablePath != null)
                {
                    string executablePath = Path.Combine(directoryPath, config.ExecutablePath);
                    if (!File.Exists(executablePath))
                    {
                        throw new FileNotFoundException($"Executable specified in updaemon.json not found: {executablePath}");
                    }

                    return executablePath;
                }
            }

            // Search for executable files matching the service name
            string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

            // First, try exact name match
            string? exactMatch = files.FirstOrDefault(f =>
                Path.GetFileName(f).Equals(serviceName, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
            {
                return exactMatch;
            }

            // Then try partial name match
            string? partialMatch = files.FirstOrDefault(f =>
                Path.GetFileName(f).Contains(serviceName, StringComparison.OrdinalIgnoreCase));

            return partialMatch;
        }
    }
}

