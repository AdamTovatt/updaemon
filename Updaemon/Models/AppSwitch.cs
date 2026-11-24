using Updaemon.Configuration;

namespace Updaemon.Models
{
    /// <summary>
    /// What <see cref="Services.AppSwitcher"/> makes current in one go: an app's own version, changes to its
    /// extensions, or both, never nothing. Each version is in its version directory already.
    /// </summary>
    public sealed class AppSwitch
    {
        private AppSwitch(PreparedApp prepared, NextAppVersion? next, IReadOnlyList<ExtensionChange> extensions, IfDown ifDown)
        {
            Prepared = prepared;
            Next = next;
            Extensions = extensions;
            IfDown = ifDown;
        }

        public PreparedApp Prepared { get; }

        public InstalledApp App => Prepared.App;

        /// <summary>Null when the app stays at its current version.</summary>
        public NextAppVersion? Next { get; }

        /// <summary>None for an app by itself.</summary>
        public IReadOnlyList<ExtensionChange> Extensions { get; }

        /// <summary>Whether all it does is take extensions out.</summary>
        public bool OnlyRemoves => Next == null && Extensions.All(extension => extension is ExtensionChange.Remove);

        public IfDown IfDown { get; }

        /// <summary>The packages it switches, the app itself first.</summary>
        public IReadOnlyList<PackageName> Names
        {
            get
            {
                IEnumerable<PackageName> extensions = Extensions.Select(extension => extension.Name);
                return (Next == null ? extensions : extensions.Prepend(App.PackageName)).ToList();
            }
        }

        public static AppSwitch Update(PreparedApp app, DeployableVersion newVersion, params IReadOnlyList<AcceptedExtension> extensions) =>
            new AppSwitch(app, NextAppVersion.ToUpdateTo(newVersion, app.App.Name), extensions.Select(Switch(app)).ToList(), IfDown.GoBack);

        public static AppSwitch UpdateExtensions(PreparedApp host, params IReadOnlyList<AcceptedExtension> extensions) => extensions.Count == 0
            ? throw new InvalidOperationException($"There is no extension of {host.App.Name} to switch.")
            : new AppSwitch(host, null, extensions.Select(Switch(host)).ToList(), IfDown.GoBack);

        public static AppSwitch RollBack(PreparedApp app, DeployableVersion version) =>
            new AppSwitch(app, new NextAppVersion(version, $"updaemon rollback {app.App.Name} {version.Version}"), Array.Empty<ExtensionChange>(), IfDown.Stand);

        public static AppSwitch RollBack(PreparedApp host, AcceptedExtension extension) =>
            new AppSwitch(host, null, new[] { Switch(host)(extension) }, IfDown.Stand);

        public static AppSwitch Install(PreparedApp host, AcceptedExtension extension, PackageSource source) =>
            new AppSwitch(host, null, new[] { new ExtensionChange.Install(extension, source) }, IfDown.GoBack);

        public static AppSwitch Remove(PreparedApp host, InstalledExtension extension) =>
            new AppSwitch(host, null, new[] { new ExtensionChange.Remove(host.App.NameOf(extension)) }, IfDown.Stand);

        private static Func<AcceptedExtension, ExtensionChange> Switch(PreparedApp host) => extension =>
            host.App.FindExtension(extension.Name.Id) == null
                ? throw new InvalidOperationException($"{extension.Name} is not installed.")
                : new ExtensionChange.Switch(extension);
    }

    /// <summary>What becomes of a switch when the service does not stay up with it.</summary>
    public enum IfDown
    {
        /// <summary>Everything is put back, and the versions switched to are skipped.</summary>
        GoBack,

        /// <summary>It stands: it was made by hand.</summary>
        Stand,
    }

    /// <summary>The version a switch makes the app's current one.</summary>
    /// <param name="RetryCommand">What to run for the same once what was in its way is not.</param>
    public sealed record NextAppVersion(DeployableVersion Version, string RetryCommand)
    {
        public static NextAppVersion ToUpdateTo(DeployableVersion version, string appName) => new NextAppVersion(version, $"updaemon update {appName}");
    }

    /// <summary>What a switch does with one extension.</summary>
    public abstract record ExtensionChange
    {
        private ExtensionChange()
        {
        }

        public abstract ExtensionName Name { get; }

        /// <summary>What becomes current, as its host accepted it; null when nothing does.</summary>
        public abstract AcceptedExtension? To { get; }

        /// <summary>Makes the first version of one current that is not in the host's entry yet.</summary>
        public sealed record Install(AcceptedExtension Accepted, PackageSource Source) : ExtensionChange
        {
            public override ExtensionName Name => Accepted.Name;

            public override AcceptedExtension? To => Accepted;
        }

        /// <summary>Makes another version of an installed one current.</summary>
        public sealed record Switch(AcceptedExtension Accepted) : ExtensionChange
        {
            public override ExtensionName Name => Accepted.Name;

            public override AcceptedExtension? To => Accepted;
        }

        public sealed record Remove(ExtensionName Extension) : ExtensionChange
        {
            public override ExtensionName Name => Extension;

            public override AcceptedExtension? To => null;
        }
    }

    /// <summary>What an <see cref="AppSwitch"/> does with one package, the app or an extension of it, with what was current of it.</summary>
    public abstract record PackageSwitch
    {
        private PackageSwitch(PackageName package)
        {
            Package = package;
        }

        public PackageName Package { get; }

        /// <summary>Makes the app's entry what it is once the switch stands.</summary>
        internal abstract void Record(InstalledApp entry);

        /// <summary>From one version to another.</summary>
        public sealed record Moved(PackageName Name, Version From, Version To) : PackageSwitch(Name)
        {
            internal override void Record(InstalledApp entry) => FindIn(entry).RecordSwitch(From, To);

            /// <summary>Skips <see cref="To"/> in an entry from before the switch.</summary>
            internal void Skip(InstalledApp entry) => FindIn(entry).RecordSwitch(from: To, to: From);

            private InstalledPackage FindIn(InstalledApp entry) =>
                entry.Find(Name) ?? throw new InvalidOperationException($"{Name} is not installed.");
        }

        public sealed record Installed(ExtensionName Name, Version To, PackageSource Source) : PackageSwitch(Name)
        {
            internal override void Record(InstalledApp entry) => entry.Extensions.Add(new InstalledExtension { Id = Name.Id, Source = Source });
        }

        public sealed record Removed(ExtensionName Name) : PackageSwitch(Name)
        {
            internal override void Record(InstalledApp entry) => entry.Extensions.RemoveAll(extension => entry.NameOf(extension) == Name);
        }
    }
}
