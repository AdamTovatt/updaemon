namespace Updaemon.Models
{
    /// <summary>
    /// A downloaded version that is not yet the app's current version.
    /// </summary>
    public sealed record StagedVersion
    {
        public required Version Version { get; init; }

        public required string Directory { get; init; }

        /// <summary>The manifest this version runs with.</summary>
        public required AppManifest Manifest { get; init; }

        /// <summary>True when the release shipped its own manifest; false when it came from the install flags.</summary>
        public required bool ManifestFromRelease { get; init; }
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
