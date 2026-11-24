using Updaemon.Services;

namespace Updaemon.Models
{
    /// <summary>
    /// What <see cref="AppSwitcher"/> makes current in one go: an app's own version, some of its
    /// extensions', or both. Each version is in its version directory already.
    /// </summary>
    /// <param name="NewVersion">The app's next version; null when it stays at its current one.</param>
    /// <param name="Extensions">The extensions to switch; none for an app by itself.</param>
    public sealed record AppSwitch(PreparedApp Prepared, DeployableVersion? NewVersion, IReadOnlyList<ExtensionSwitch> Extensions)
    {
        public InstalledApp App => Prepared.App;

        public static AppSwitch ToVersion(PreparedApp app, DeployableVersion version) => new AppSwitch(app, version, Array.Empty<ExtensionSwitch>());

        public static AppSwitch OfExtension(PreparedApp host, InstalledExtension extension, Version? to) =>
            new AppSwitch(host, null, new[] { new ExtensionSwitch(extension, to) });
    }

    /// <param name="Extension">One of the host's, or one that is not in its entry yet: the switch then installs it.</param>
    /// <param name="To">Null to remove it.</param>
    public sealed record ExtensionSwitch(InstalledExtension Extension, Version? To);

    /// <summary>One package of an <see cref="AppSwitch"/>, with what was current of it.</summary>
    /// <param name="From">Null for an extension the switch installs.</param>
    /// <param name="To">Null for an extension the switch removes.</param>
    public sealed record PackageSwitch(PackageName Name, InstalledPackage Package, Version? From, Version? To)
    {
        /// <summary>The switch's effect on which versions update passes over; none when it installs or removes.</summary>
        public void Record()
        {
            if (From != null && To != null)
            {
                Package.RecordSwitch(From, To);
            }
        }

        /// <summary>The effect of going back.</summary>
        public void RecordGoingBack()
        {
            if (From != null && To != null)
            {
                Package.RecordSwitch(from: To, to: From);
            }
        }
    }
}
