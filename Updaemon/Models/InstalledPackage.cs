using System.Text.Json.Serialization;

namespace Updaemon.Models
{
    /// <summary>
    /// What updaemon installs releases of, an app or an extension of one, as stored in the scope's
    /// config: where its releases come from and which of them update passes over.
    /// </summary>
    public abstract class InstalledPackage
    {
        /// <summary>Alias of the distribution plugin it is downloaded with.</summary>
        public string Plugin { get; set; } = string.Empty;

        /// <summary>What the plugin calls it, for example <c>owner/repo</c> for GitHub.</summary>
        public string Remote { get; set; } = string.Empty;

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
            // Internal, not private, so the JSON source generator can set it when reading the config.
            internal set => _skippedVersions = value.Order().ToList();
        }

        /// <summary><c>&lt;plugin&gt;:&lt;remote&gt;</c>, as given to <c>install --from</c>.</summary>
        [JsonIgnore]
        public PackageSource Source
        {
            get => new PackageSource(Plugin, Remote);
            init => (Plugin, Remote) = (value.Plugin, value.Remote);
        }

        public bool IsSkipped(Version version) => _skippedVersions.Contains(version);

        /// <summary>
        /// After <paramref name="to"/> became current in place of <paramref name="from"/>: <paramref name="to"/>
        /// is no longer skipped, and going back skips <paramref name="from"/>.
        /// </summary>
        public void RecordSwitch(Version from, Version to)
        {
            List<Version> skipped = _skippedVersions.Where(version => version != to).ToList();
            if (to < from && !skipped.Contains(from))
            {
                skipped.Add(from);
                skipped.Sort();
            }

            _skippedVersions = skipped;
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
