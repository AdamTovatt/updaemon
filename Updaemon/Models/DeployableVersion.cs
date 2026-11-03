namespace Updaemon.Models
{
    /// <summary>
    /// A version ready to become the app's current version: just downloaded (staged), or retained from before.
    /// </summary>
    public sealed record DeployableVersion
    {
        public required Version Version { get; init; }

        public required string Directory { get; init; }

        /// <summary>The manifest this version runs with.</summary>
        public required AppManifest Manifest { get; init; }

        /// <summary>
        /// True when the version directory already holds <see cref="Manifest"/>: the release shipped it, or
        /// the version is a retained one. False when it comes from the install flags; activation writes it.
        /// </summary>
        public required bool ManifestInDirectory { get; init; }
    }

    /// <summary>
    /// A release with no <c>updaemon.json</c>, for an app that has no manifest from install flags either.
    /// </summary>
    public sealed class MissingManifestException : Exception
    {
        public MissingManifestException(string message)
            : base(message)
        {
        }
    }
}
