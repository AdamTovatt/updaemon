using System.Runtime.InteropServices;
using System.Text.Json;
using Updaemon.Interfaces;
using Updaemon.Serialization;

namespace Updaemon.Services
{
    /// <summary>
    /// Resolves plugin names to URLs by fetching a registry file from GitHub.
    /// The registry maps each plugin alias to a per-RID URL map so that plugins can
    /// ship separate binaries per platform (e.g. linux-arm64, osx-arm64).
    /// </summary>
    public class GitHubPluginUrlResolver : IPluginUrlResolver
    {
        private const string RegistryUrl = "https://raw.githubusercontent.com/AdamTovatt/updaemon/master/PluginRegistry.json";
        private readonly HttpClient _httpClient;
        private readonly string _runtimeIdentifier;

        public GitHubPluginUrlResolver(HttpClient httpClient)
            : this(httpClient, ResolveCurrentRid())
        {
        }

        public GitHubPluginUrlResolver(HttpClient httpClient, string runtimeIdentifier)
        {
            _httpClient = httpClient;
            _runtimeIdentifier = runtimeIdentifier;
        }

        public async Task<string> ResolveAsync(string pluginName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentException("Plugin name cannot be null or empty.", nameof(pluginName));
            }

            string jsonContent;
            try
            {
                jsonContent = await _httpClient.GetStringAsync(RegistryUrl, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to fetch plugin registry: {ex.Message}. " +
                    "You can try installing the plugin using the full URL instead.",
                    ex);
            }

            Dictionary<string, Dictionary<string, string>>? registry;
            try
            {
                registry = JsonSerializer.Deserialize(jsonContent, UpdaemonJsonContext.Default.DictionaryStringDictionaryStringString);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    "Failed to parse plugin registry. The registry file may be invalid. " +
                    "You can try installing the plugin using the full URL instead.",
                    ex);
            }

            if (registry == null)
            {
                throw new InvalidOperationException(
                    "Failed to parse plugin registry. The registry file may be invalid. " +
                    "You can try installing the plugin using the full URL instead.");
            }

            if (!registry.TryGetValue(pluginName, out Dictionary<string, string>? perRidUrls) || perRidUrls == null || perRidUrls.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Plugin '{pluginName}' not found in the registry. " +
                    "Check the registry at https://github.com/AdamTovatt/updaemon/blob/master/PluginRegistry.json " +
                    "or install the plugin using the full URL instead.");
            }

            if (!perRidUrls.TryGetValue(_runtimeIdentifier, out string? url) || string.IsNullOrWhiteSpace(url))
            {
                string available = string.Join(", ", perRidUrls.Keys);
                throw new InvalidOperationException(
                    $"Plugin '{pluginName}' has no build for runtime '{_runtimeIdentifier}'. " +
                    $"Available builds: {available}. " +
                    "You can try installing the plugin using the full URL instead.");
            }

            return url;
        }

        private static string ResolveCurrentRid()
        {
            string os = true switch
            {
                _ when OperatingSystem.IsMacOS() => "osx",
                _ when OperatingSystem.IsLinux() => "linux",
                _ when OperatingSystem.IsWindows() => "win",
                _ => "unknown",
            };

            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => "unknown",
            };

            return $"{os}-{arch}";
        }
    }
}
