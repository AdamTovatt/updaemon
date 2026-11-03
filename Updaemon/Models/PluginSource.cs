using System.Text.Json.Serialization;

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

        /// <summary>
        /// A first-party plugin named in PluginRegistry.json.
        /// </summary>
        public sealed record Registry(string Name) : PluginSource
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
