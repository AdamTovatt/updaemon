namespace Updaemon.Models
{
    /// <summary>
    /// Saved in the app's entry before its versions are switched and cleared once they are, so a run
    /// that was killed in between is put right by the next one (see <see cref="Services.AppSwitcher"/>).
    /// </summary>
    public sealed class UnfinishedSwitch
    {
        /// <summary>What to make current again. Empty once the switch is there to stay and only the restart is left.</summary>
        public List<VersionBefore> PutBack { get; set; } = new List<VersionBefore>();

        /// <summary>Whether the switch restarts the service.</summary>
        public bool Restart { get; set; }

        /// <summary>The service as it was, when the switch removes it: to a version that is none.</summary>
        public ServiceBefore? RemovedService { get; set; }
    }

    /// <summary>What was current before a switch.</summary>
    public sealed class VersionBefore
    {
        /// <summary>The extension's id; null for the app itself.</summary>
        public string? Extension { get; set; }

        /// <summary>The version that was current; null for an extension the switch installs.</summary>
        public Version? Version { get; set; }

        public PackageName NameIn(InstalledApp app) => Extension == null ? app.PackageName : new ExtensionName(app.Name, Extension);
    }

    /// <summary>What a service was like before a switch.</summary>
    public sealed class ServiceBefore
    {
        public bool Enabled { get; set; }

        public bool Running { get; set; }
    }
}
