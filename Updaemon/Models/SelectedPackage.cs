namespace Updaemon.Models
{
    /// <summary>An installed app, or one extension of it.</summary>
    public sealed record SelectedPackage(InstalledApp App, InstalledExtension? Extension)
    {
        /// <summary>Its full name, as commands take it.</summary>
        public PackageName Name => Extension == null ? App.PackageName : App.NameOf(Extension);

        public InstalledPackage Package => (InstalledPackage?)Extension ?? App;
    }
}
