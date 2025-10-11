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
        public async Task<string?> FindExecutableAsync(string directoryPath, string serviceName)
        {
            if (!Directory.Exists(directoryPath))
            {
                return null;
            }

            // First, check if there's an updaemon.json config file
            string configPath = Path.Combine(directoryPath, "updaemon.json");
            if (File.Exists(configPath))
            {
                try
                {
                    string configJson = await File.ReadAllTextAsync(configPath);
                    AppConfig? config = JsonSerializer.Deserialize(configJson, UpdaemonJsonContext.Default.AppConfig);

                    if (config?.ExecutablePath != null)
                    {
                        string executablePath = Path.Combine(directoryPath, config.ExecutablePath);
                        if (File.Exists(executablePath) && IsExecutable(executablePath))
                        {
                            return executablePath;
                        }
                    }
                }
                catch
                {
                    // If config parsing fails, fall back to searching
                }
            }

            // Search for executable files matching the service name
            string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

            // First, try exact name match
            string? exactMatch = files.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(serviceName, StringComparison.OrdinalIgnoreCase)
                && IsExecutable(f));

            if (exactMatch != null)
            {
                return exactMatch;
            }

            // Then try partial name match
            string? partialMatch = files.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Contains(serviceName, StringComparison.OrdinalIgnoreCase)
                && IsExecutable(f));

            return partialMatch;
        }

        private bool IsExecutable(string filePath)
        {
            try
            {
                // On Linux, check if file has execute permission
                // This is a simplified check; on Linux we'd need to use P/Invoke or similar
                FileInfo fileInfo = new FileInfo(filePath);

                // For now, consider files without extensions or common executable extensions as executable
                string extension = fileInfo.Extension.ToLowerInvariant();
                return string.IsNullOrEmpty(extension) ||
                       extension == ".sh" ||
                       extension == ".bin" ||
                       !extension.Contains('.');
            }
            catch
            {
                return false;
            }
        }
    }
}

