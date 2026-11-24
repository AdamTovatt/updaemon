namespace Updaemon.Models
{
    /// <summary>
    /// What an app or an extension is called, as commands take it and messages name it. Which of the two it
    /// is decides where its versions are (see <see cref="Configuration.UpdaemonScope.Locate"/>).
    /// </summary>
    public abstract record PackageName;

    /// <summary>An app's name: <c>byteshelf</c>.</summary>
    public sealed record AppName(string Name) : PackageName
    {
        public override string ToString() => Name;
    }

    /// <summary>An extension's name: <c>&lt;host&gt;/&lt;id&gt;</c>, the app that loads it and its id there.</summary>
    public sealed record ExtensionName(string Host, string Id) : PackageName
    {
        private const char Separator = '/';

        /// <summary>Whether <paramref name="name"/> has the shape of an extension's name, which no app's has.</summary>
        public static bool IsMeantBy(string name) => name.Contains(Separator);

        /// <summary>Reads <c>&lt;host&gt;/&lt;id&gt;</c>; null when <paramref name="name"/> is not two valid app names around one slash.</summary>
        public static ExtensionName? TryParse(string name)
        {
            return name.Split(Separator) is [string host, string id] && AppManifest.IsValidAppName(host) && AppManifest.IsValidAppName(id)
                ? new ExtensionName(host, id)
                : null;
        }

        public override string ToString() => $"{Host}{Separator}{Id}";
    }
}
