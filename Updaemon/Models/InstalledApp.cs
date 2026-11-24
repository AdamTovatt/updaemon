using System.Text.Json;
using System.Text.Json.Serialization;
using Updaemon.Serialization;

namespace Updaemon.Models
{
    /// <summary>
    /// An app installed with <c>updaemon install</c>, as stored in the scope's config.
    /// What the app is (command, service or both) comes from its manifest, not from here.
    /// </summary>
    public sealed class InstalledApp : InstalledPackage
    {
        /// <summary>Local name: the app's directory, unit and data directory are named after it.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The manifest built from <c>install --command/--service</c>, used for releases that ship
        /// none. Null once a release ships its own.
        /// </summary>
        public AppManifest? Manifest { get; set; }

        /// <summary>Non-secret setting values by key. Secret values live in the app's secrets file.</summary>
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();

        /// <summary>Service arguments set with <c>set-args</c>; null means the manifest's.</summary>
        public List<string>? Args { get; set; }

        /// <summary>The user the service runs as in the system scope; null means root. Always null in the user scope.</summary>
        public string? RunAs { get; set; }

        /// <summary>
        /// Whether the service is restarted after an update deploys a new version. When false, the new
        /// version is deployed but the running process is left alone until it restarts on its own.
        /// </summary>
        public bool AutoRestart { get; set; } = true;

        /// <summary>The extensions installed into this app, their host.</summary>
        public List<InstalledExtension> Extensions { get; set; } = new List<InstalledExtension>();

        /// <summary>A change that has begun and not ended; what is here after a run ended is left to the next one.</summary>
        public UnfinishedChange? Unfinished { get; set; }

        [JsonIgnore]
        public AppName PackageName => new AppName(Name);

        /// <summary>Ids that differ only in case are one: on macOS, so are their directories.</summary>
        public InstalledExtension? FindExtension(string id) => Extensions.FirstOrDefault(extension => string.Equals(extension.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>The extension's full name, as commands take it.</summary>
        public ExtensionName NameOf(InstalledExtension extension) => new ExtensionName(Name, extension.Id);

        /// <summary>The package of that name: this app or one of its extensions.</summary>
        public InstalledPackage? Find(PackageName name) => name is ExtensionName extension ? FindExtension(extension.Id) : this;

        /// <summary>An entry of its own, down to each extension and setting: editing one leaves the other.</summary>
        public InstalledApp Copy() => JsonSerializer.Deserialize(ToJson(), UpdaemonJsonContext.Default.InstalledApp)!;

        /// <summary>Whether both would be saved as the same.</summary>
        public bool IsSameAs(InstalledApp other) => ToJson() == other.ToJson();

        private string ToJson() => JsonSerializer.Serialize(this, UpdaemonJsonContext.Default.InstalledApp);
    }
}
