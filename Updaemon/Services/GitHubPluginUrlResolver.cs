using System.Text.Json;
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
        private const string ReleaseDownloadUrl = "https://github.com/AdamTovatt/updaemon/releases/download";
        private const string EmbeddedRegistryName = "PluginAssets.json";

        private readonly IReadOnlyDictionary<string, string> _assetBaseNames;
        private readonly string _runtimeIdentifier;

        /// <param name="assetBaseNames">Plugin name to asset base name, as in PluginRegistry.json.</param>
        /// <param name="runtimeIdentifier">This machine's runtime identifier, e.g. "linux-arm64".</param>
        public GitHubPluginUrlResolver(IReadOnlyDictionary<string, string> assetBaseNames, string runtimeIdentifier)
        {
            _assetBaseNames = assetBaseNames;
            _runtimeIdentifier = runtimeIdentifier;
        }

        public IReadOnlyCollection<string> PluginNames => _assetBaseNames.Keys.ToList();

        public string? Resolve(string pluginName, Version updaemonVersion)
        {
            if (!_assetBaseNames.TryGetValue(pluginName, out string? assetBaseName))
            {
                return null;
            }

            string tag = $"v{updaemonVersion.Major}.{updaemonVersion.Minor}.{Math.Max(updaemonVersion.Build, 0)}";
            return $"{ReleaseDownloadUrl}/{tag}/{assetBaseName}-{_runtimeIdentifier}";
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
