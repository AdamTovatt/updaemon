namespace Updaemon.Models
{
    /// <summary>Whether a version of a host loads a version of an extension, as <see cref="Services.ExtensionCheck"/> found.</summary>
    public abstract class ExtensionVerdict
    {
        private protected ExtensionVerdict(Version hostVersion, ExtensionName name, Version version)
        {
            HostVersion = hostVersion;
            Name = name;
            Version = version;
        }

        public Version HostVersion { get; }

        public ExtensionName Name { get; }

        public Version Version { get; }
    }

    /// <summary>
    /// Proof that <see cref="ExtensionVerdict.HostVersion"/> loads that version of the extension. Only
    /// <see cref="Services.ExtensionCheck"/> makes one, and what makes an extension's version current takes one.
    /// </summary>
    public sealed class AcceptedExtension : ExtensionVerdict
    {
        internal AcceptedExtension(Version hostVersion, ExtensionName name, Version version)
            : base(hostVersion, name, version)
        {
        }
    }

    /// <summary>It does not.</summary>
    public sealed class ExtensionRefusal : ExtensionVerdict
    {
        internal ExtensionRefusal(Version hostVersion, ExtensionName name, Version version, string reason)
            : base(hostVersion, name, version)
        {
            Reason = reason;
        }

        /// <summary>A sentence part, without a period.</summary>
        public string Reason { get; }

        /// <summary>A sentence without its period. The caller names the extension in front.</summary>
        public override string ToString() => $"{Name.Host} {HostVersion} refuses {Version}: {Reason}";
    }
}
