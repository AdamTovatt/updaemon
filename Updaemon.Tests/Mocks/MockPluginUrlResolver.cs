using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IPluginUrlResolver for testing.
    /// </summary>
    public class MockPluginUrlResolver : IPluginUrlResolver
    {
        private readonly Dictionary<string, string> _registry = new Dictionary<string, string>();
        public List<string> MethodCalls { get; } = new List<string>();

        public void SetPluginUrl(string pluginName, string url)
        {
            _registry[pluginName] = url;
        }

        public Task<string> ResolveAsync(string pluginName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"ResolveAsync:{pluginName}");

            if (!_registry.TryGetValue(pluginName, out string? url) || string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException(
                    $"Plugin '{pluginName}' not found in the registry. " +
                    "Check the registry at https://github.com/AdamTovatt/updaemon/blob/master/PluginRegistry.json " +
                    "or install the plugin using the full URL instead.");
            }

            return Task.FromResult(url);
        }
    }
}

