namespace Updaemon.Models
{
    /// <summary>An extension of an installed app, kept in that app's entry: it shares its scope and its lifetime.</summary>
    public sealed class InstalledExtension : InstalledPackage
    {
        /// <summary>Its name after the slash, as in <c>byteshelf/images</c>.</summary>
        public string Id { get; set; } = string.Empty;
    }
}
