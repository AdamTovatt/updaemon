namespace Updaemon.Models
{
    /// <summary>
    /// What <see cref="Services.AppSwitcher"/> makes current in one go: an app's own version, some of its
    /// extensions', or both, never nothing. Each version is in its version directory already.
    /// </summary>
    public sealed class AppSwitch
    {
        private AppSwitch(PreparedApp prepared, DeployableVersion? newVersion, IReadOnlyList<ExtensionSwitch> extensions)
        {
            Prepared = prepared;
            NewVersion = newVersion;
            Extensions = extensions;
        }

        public PreparedApp Prepared { get; }

        public InstalledApp App => Prepared.App;

        /// <summary>The app's next version; null when it stays at its current one.</summary>
        public DeployableVersion? NewVersion { get; }

        /// <summary>The extensions to switch; none for an app by itself.</summary>
        public IReadOnlyList<ExtensionSwitch> Extensions { get; }

        /// <summary>The packages it switches, the app itself first.</summary>
        public IReadOnlyList<PackageName> Names
        {
            get
            {
                IEnumerable<PackageName> extensions = Extensions.Select(extension => App.NameOf(extension.Extension));
                return (NewVersion == null ? extensions : extensions.Prepend(App.PackageName)).ToList();
            }
        }

        public static AppSwitch ToVersion(PreparedApp app, DeployableVersion version) => new AppSwitch(app, version, Array.Empty<ExtensionSwitch>());

        public static AppSwitch OfExtension(PreparedApp host, InstalledExtension extension, Version to) =>
            new AppSwitch(host, null, new[] { new ExtensionSwitch(extension, to) });

        /// <summary>Null when there is neither a new version nor an extension to switch.</summary>
        public static AppSwitch? OfAll(PreparedApp app, DeployableVersion? newVersion, IReadOnlyList<ExtensionSwitch> extensions) =>
            newVersion == null && extensions.Count == 0 ? null : new AppSwitch(app, newVersion, extensions);
    }

    /// <param name="Extension">One of the host's, or one that is not in its entry yet: the switch then installs it.</param>
    public sealed record ExtensionSwitch(InstalledExtension Extension, Version To);

    /// <summary>One package of an <see cref="AppSwitch"/>, with what was current of it.</summary>
    /// <param name="From">Null for an extension the switch installs.</param>
    public sealed record PackageSwitch(PackageName Name, InstalledPackage Package, Version? From, Version To)
    {
        /// <summary>The switch's effect on which versions update passes over; none when it installs.</summary>
        public void Record()
        {
            if (From != null)
            {
                Package.RecordSwitch(From, To);
            }
        }

        /// <summary>The effect of going back.</summary>
        public void RecordGoingBack()
        {
            if (From != null)
            {
                Package.RecordSwitch(from: To, to: From);
            }
        }
    }
}
