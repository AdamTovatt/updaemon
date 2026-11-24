using System.Text.Json.Serialization;

namespace Updaemon.Models
{
    /// <summary>
    /// Saved in the app's entry while a change to the app is under way and cleared once it is done, so a run
    /// that was killed in between is put right by the next one (see <see cref="Services.AppPreparer"/>).
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(PutBack), "putBack")]
    [JsonDerivedType(typeof(CarryOut), "carryOut")]
    public abstract record UnfinishedChange
    {
        private UnfinishedChange()
        {
        }

        /// <summary>Whether the change restarts the service.</summary>
        public bool Restart { get; init; }

        /// <summary>The entry is as it was before the change; what was current then is to be current again.</summary>
        public sealed record PutBack : UnfinishedChange
        {
            public required List<VersionBefore> Versions { get; init; }

            /// <summary>The manifest of the app's version that is switched to; null when the app's version stays.</summary>
            public AppManifest? NewManifest { get; init; }

            /// <summary>The service as it was, when the change removes it: to a version that is none.</summary>
            public ServiceBefore? RemovedService { get; init; }
        }

        /// <summary>The entry stands as it is; the app's files, and its service if it is to be restarted, are still to follow it and the secrets.</summary>
        public sealed record CarryOut : UnfinishedChange;
    }

    /// <summary>What was current before a change.</summary>
    public sealed class VersionBefore
    {
        /// <summary>The extension's id; null for the app itself.</summary>
        public string? Extension { get; set; }

        /// <summary>The version that was current; null for an extension that had none, as one the change installs.</summary>
        public Version? Version { get; set; }

        public PackageName NameIn(InstalledApp app) => Extension == null ? app.PackageName : new ExtensionName(app.Name, Extension);
    }

    /// <summary>What a service was like before a change.</summary>
    public sealed class ServiceBefore
    {
        public bool Enabled { get; set; }

        public bool Running { get; set; }
    }
}
