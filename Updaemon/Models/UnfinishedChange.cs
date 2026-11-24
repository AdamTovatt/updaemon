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
            /// <summary>Null when the app's own version stays.</summary>
            public AppBefore? App { get; init; }

            /// <summary>The extensions that are switched.</summary>
            public required List<ExtensionBefore> Extensions { get; init; }
        }

        /// <summary>The entry stands as it is; the app's files, and its service if it is to be restarted, are still to follow it and the secrets.</summary>
        public sealed record CarryOut : UnfinishedChange;
    }

    /// <summary>What an app was like before a change of its own version.</summary>
    public sealed class AppBefore
    {
        /// <summary>The version that was current.</summary>
        public required Version Version { get; init; }

        /// <summary>The manifest of the version that is switched to.</summary>
        public required AppManifest NewManifest { get; init; }

        /// <summary>The service as it was, when the change removes it: to a version that is none.</summary>
        public ServiceBefore? RemovedService { get; init; }
    }

    /// <summary>What an extension was like before a change.</summary>
    public sealed class ExtensionBefore
    {
        public required string Id { get; init; }

        /// <summary>The version that was current; null for one that had none, as one the change installs.</summary>
        public Version? Version { get; init; }
    }

    /// <summary>What a service was like before a change.</summary>
    public sealed class ServiceBefore
    {
        public required bool Enabled { get; init; }

        public required bool Running { get; init; }
    }
}
