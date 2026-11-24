namespace Updaemon.Models
{
    /// <summary>Where an app's or an extension's versions are on disk.</summary>
    /// <param name="AppDirectory">The app's own directory, or the directory of the app an extension belongs to.</param>
    /// <param name="VersionsDirectory">Holds one directory per version, named by it.</param>
    /// <param name="CurrentLink">The link to the current version's directory.</param>
    public sealed record PackageLocation(string AppDirectory, string VersionsDirectory, string CurrentLink);
}
