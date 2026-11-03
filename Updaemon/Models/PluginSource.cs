using System.Text.Json.Serialization;
using Updaemon.Configuration;

namespace Updaemon.Models
{
    /// <summary>
    /// Where a plugin is installed from: updaemon's plugin registry, by name, or a URL.
    /// A registry plugin comes from updaemon's own release, so updates follow updaemon's;
    /// a URL plugin is only ever replaced by hand.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(Registry), "registry")]
    [JsonDerivedType(typeof(Url), "url")]
    public abstract record PluginSource
    {
        // Private so the two kinds below are the only ones.
        private PluginSource()
        {
        }

        /// <summary>Whether updating updaemon brings this plugin along; a URL plugin is replaced only by hand.</summary>
        [JsonIgnore]
        public bool FollowsUpdaemon => this is Registry;

        /// <summary>
        /// Whether this is a registry plugin built for another updaemon than <paramref name="running"/>.
        /// Such a plugin is not run: it is reinstalled from the running updaemon's release first (see
        /// <see cref="Services.PluginProvider"/>).
        /// </summary>
        public bool IsOutOfLineWith(UpdaemonVersion running) => this is Registry registry && registry.Release != running;

        /// <summary>A first-party plugin named in the registry, built in updaemon's release <paramref name="Release"/>.</summary>
        public sealed record Registry(string Name, [property: JsonRequired] UpdaemonVersion Release) : PluginSource
        {
            public override string ToString() => Name;
        }

        /// <summary>
        /// A plugin binary downloaded from an address the user gave.
        /// </summary>
        public sealed record Url(string Address) : PluginSource
        {
            public override string ToString() => Address;
        }
    }
}
