using System.Text.Json.Serialization;

namespace Updaemon.Models
{
    /// <summary>
    /// An app installed with <c>updaemon install</c>, as stored in the scope's config.
    /// What the app is (command, service or both) comes from its manifest, not from here.
    /// </summary>
    public class InstalledApp
    {
        /// <summary>Local name: the app's directory, unit and data directory are named after it.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Alias of the distribution plugin the app is downloaded with.</summary>
        public string Plugin { get; set; } = string.Empty;

        /// <summary>What the plugin calls the app, for example <c>owner/repo</c> for GitHub.</summary>
        public string Remote { get; set; } = string.Empty;

        /// <summary>
        /// The manifest built from <c>install --command/--service</c>, used for releases that ship
        /// none. Null once a release ships its own.
        /// </summary>
        public AppManifest? Manifest { get; set; }

        /// <summary>Non-secret setting values by key. Secret values live in the app's secrets file.</summary>
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();

        /// <summary>Service arguments set with <c>set-args</c>; null means the manifest's.</summary>
        public List<string>? Args { get; set; }

        /// <summary>The user the service runs as in the system scope; null means root. Always null in the user scope.</summary>
        public string? RunAs { get; set; }

        /// <summary>
        /// Whether the service is restarted after an update deploys a new version. When false, the new
        /// version is deployed but the running process is left alone until it restarts on its own.
        /// </summary>
        public bool AutoRestart { get; set; } = true;

        private List<Version> _skippedVersions = new List<Version>();

        /// <summary>
        /// Versions <c>update</c> passes over and <c>rollback</c> does not pick by itself: each failed its
        /// health check or was rolled back from. Oldest first. The current version is never among them:
        /// only <see cref="RecordSwitch"/> and <see cref="ForgetSkipsOlderThan"/> change the list.
        /// </summary>
        [JsonInclude]
        public IReadOnlyList<Version> SkippedVersions
        {
            get => _skippedVersions;
            // Internal, not private, only so the JSON source generator can set it when reading the config.
            internal set => _skippedVersions = value.Order().ToList();
        }

        /// <summary><c>&lt;plugin&gt;:&lt;remote&gt;</c>, as given to <c>install --from</c>.</summary>
        [JsonIgnore]
        public string Source => $"{Plugin}:{Remote}";

        public bool IsSkipped(Version version) => _skippedVersions.Contains(version);

        /// <summary>
        /// After <paramref name="to"/> became current in place of <paramref name="from"/>: <paramref name="to"/>
        /// is no longer skipped, and going back skips <paramref name="from"/>.
        /// </summary>
        public void RecordSwitch(Version from, Version to)
        {
            _skippedVersions.Remove(to);
            if (to < from && !_skippedVersions.Contains(from))
            {
                _skippedVersions.Add(from);
                _skippedVersions.Sort();
            }
        }

        /// <summary>
        /// Update never offers a version older than <paramref name="current"/> again, so such a skip only
        /// keeps rollback away from it, and only while it is kept. Returns whether any skip was dropped.
        /// </summary>
        public bool ForgetSkipsOlderThan(Version current, IReadOnlyList<Version> kept)
        {
            return _skippedVersions.RemoveAll(skipped => skipped < current && !kept.Contains(skipped)) > 0;
        }

        /// <summary>The newest kept version older than <paramref name="current"/> that is not skipped.</summary>
        public Version? FindRollbackTarget(Version current, IReadOnlyList<Version> kept)
        {
            return kept.Where(version => version < current && !IsSkipped(version)).Max();
        }
    }
}
