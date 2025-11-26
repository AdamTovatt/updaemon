using System.Text.Json;
using Updaemon.Interfaces;
using Updaemon.Serialization;

namespace Updaemon.Services
{
    /// <summary>
    /// Resolves plugin names to URLs by fetching a registry file from GitHub.
    /// </summary>
    public class GitHubPluginUrlResolver : IPluginUrlResolver
    {
        private const string RegistryUrl = "https://raw.githubusercontent.com/AdamTovatt/updaemon/master/PluginRegistry.json";
        private readonly HttpClient _httpClient;

        public GitHubPluginUrlResolver(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> ResolveAsync(string pluginName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentException("Plugin name cannot be null or empty.", nameof(pluginName));
            }

            try
            {
                string jsonContent = await _httpClient.GetStringAsync(RegistryUrl, cancellationToken);
                Dictionary<string, string>? registry = JsonSerializer.Deserialize(jsonContent, UpdaemonJsonContext.Default.DictionaryStringString);

                if (registry == null)
                {
                    throw new InvalidOperationException(
                        "Failed to parse plugin registry. The registry file may be invalid. " +
                        "You can try installing the plugin using the full URL instead.");
                }

                if (!registry.TryGetValue(pluginName, out string? url) || string.IsNullOrWhiteSpace(url))
                {
                    throw new InvalidOperationException(
                        $"Plugin '{pluginName}' not found in the registry. " +
                        "Check the registry at https://github.com/AdamTovatt/updaemon/blob/master/PluginRegistry.json " +
                        "or install the plugin using the full URL instead.");
                }

                return url;
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to fetch plugin registry: {ex.Message}. " +
                    "You can try installing the plugin using the full URL instead.",
                    ex);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    "Failed to parse plugin registry. The registry file may be invalid. " +
                    "You can try installing the plugin using the full URL instead.",
                    ex);
            }
        }
    }
}

