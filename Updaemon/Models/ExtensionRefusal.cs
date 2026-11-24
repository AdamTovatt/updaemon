namespace Updaemon.Models
{
    /// <summary>A version of a host does not load a version of an extension.</summary>
    /// <param name="Reason">A sentence part, without a period.</param>
    public sealed record ExtensionRefusal(string Host, Version HostVersion, Version Version, string Reason)
    {
        /// <summary>A sentence without its period. The caller names the extension in front.</summary>
        public override string ToString() => $"{Host} {HostVersion} refuses {Version}: {Reason}";
    }
}
