using Updaemon.Configuration;

namespace Updaemon.Models
{
    /// <summary>An installed app, or one extension of it, ready to be changed.</summary>
    public sealed record PreparedPackage(PreparedApp Prepared, InstalledExtension? Extension)
    {
        public SelectedPackage Package => new SelectedPackage(Prepared.App, Extension);
    }
}
