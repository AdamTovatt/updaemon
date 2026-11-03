using System.Text.Json;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Serialization;

namespace Updaemon.Services
{
    /// <summary>
    /// Resolves registry plugins to their builds in updaemon's own GitHub releases.
    /// The registry (Updaemon/PluginAssets.json, embedded at build time so it always matches
    /// the running version) maps each plugin name to an asset base name; the build for a
    /// machine is the asset &lt;base&gt;-&lt;rid&gt; in the release v&lt;version&gt;.
    /// The PluginRegistry.json at the repository root is a different, frozen file: updaemon
    /// 0.x fetches it from master.
    /// </summary>
    public class GitHubPluginUrlResolver : IPluginUrlResolver
    {
        private const string EmbeddedRegistryName = "PluginAssets.json";

        private readonly IReadOnlyDictionary<string, string> _assetBaseNames;
        private readonly string _runtimeIdentifier;

        /// <summary>
        /// Resolves against the registry compiled into this build.
        /// </summary>
        public GitHubPluginUrlResolver(HostPlatform platform)
            : this(LoadEmbeddedRegistry(), platform)
        {
        }

        /// <param name="assetBaseNames">Plugin name to asset base name, as in PluginAssets.json.</param>
        /// <param name="platform">This machine; plugins are resolved to its runtime identifier's build.</param>
        public GitHubPluginUrlResolver(IReadOnlyDictionary<string, string> assetBaseNames, HostPlatform platform)
        {
            _assetBaseNames = assetBaseNames;
            _runtimeIdentifier = platform.RuntimeIdentifier;
        }

        public IReadOnlyCollection<string> PluginNames => _assetBaseNames.Keys.ToList();

        public string? Resolve(string pluginName, UpdaemonVersion updaemonVersion)
        {
            if (!_assetBaseNames.TryGetValue(pluginName, out string? assetBaseName))
            {
                return null;
            }

            return GitHubUpdaemonReleases.AssetUrl(updaemonVersion, assetBaseName, _runtimeIdentifier);
        }

        /// <summary>
        /// Reads the registry compiled into this build.
        /// </summary>
        public static IReadOnlyDictionary<string, string> LoadEmbeddedRegistry()
        {
            using (Stream? stream = typeof(GitHubPluginUrlResolver).Assembly.GetManifestResourceStream(EmbeddedRegistryName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException($"This updaemon build is missing its embedded {EmbeddedRegistryName}.");
                }

                Dictionary<string, string>? registry = JsonSerializer.Deserialize(stream, UpdaemonJsonContext.Default.DictionaryStringString);
                if (registry == null)
                {
                    throw new InvalidOperationException($"This updaemon build's embedded {EmbeddedRegistryName} is empty.");
                }

                return registry;
            }
        }
    }
}
