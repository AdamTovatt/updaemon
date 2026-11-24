using Updaemon.Configuration;

namespace Updaemon.Models
{
    /// <summary>
    /// What <see cref="Services.AppSwitcher"/> makes current in one go: an app's own version, changes to its
    /// extensions, or both, never nothing. Each version is in its version directory already.
    /// </summary>
    public sealed class AppSwitch
    {
        private AppSwitch(PreparedApp prepared, DeployableVersion? newVersion, IReadOnlyList<ExtensionChange> extensions, IfDown ifDown, string retryCommand)
        {
            Prepared = prepared;
            NewVersion = newVersion;
            Extensions = extensions;
            IfDown = ifDown;
            RetryCommand = retryCommand;
        }

        public PreparedApp Prepared { get; }

        public InstalledApp App => Prepared.App;

        /// <summary>The app's next version; null when it stays at its current one.</summary>
        public DeployableVersion? NewVersion { get; }

        /// <summary>None for an app by itself.</summary>
        public IReadOnlyList<ExtensionChange> Extensions { get; }

        /// <summary>Whether all it does is take extensions out.</summary>
        public bool OnlyRemoves => NewVersion == null && Extensions.All(extension => extension.To == null);

        public IfDown IfDown { get; }

        /// <summary>What to run for the same once what was in its way is not.</summary>
        public string RetryCommand { get; }

        /// <summary>The packages it switches, the app itself first.</summary>
        public IReadOnlyList<PackageName> Names
        {
            get
            {
                IEnumerable<PackageName> extensions = Extensions.Select(extension => extension.Name);
                return (NewVersion == null ? extensions : extensions.Prepend(App.PackageName)).ToList();
            }
        }

        public static AppSwitch Update(PreparedApp app, DeployableVersion newVersion, params IReadOnlyList<AcceptedExtension> extensions) =>
            new AppSwitch(app, newVersion, extensions.Select(Switch(app)).ToList(), IfDown.GoBack, RetryUpdate(app.App.Name));

        public static AppSwitch UpdateExtensions(PreparedApp host, params IReadOnlyList<AcceptedExtension> extensions) => extensions.Count == 0
            ? throw new InvalidOperationException($"There is no extension of {host.App.Name} to switch.")
            : new AppSwitch(host, null, extensions.Select(Switch(host)).ToList(), IfDown.GoBack, RetryUpdate(host.App.Name));

        public static AppSwitch RollBack(PreparedApp app, DeployableVersion version) =>
            new AppSwitch(app, version, Array.Empty<ExtensionChange>(), IfDown.Stand, $"updaemon rollback {app.App.Name} {version.Version}");

        public static AppSwitch RollBack(PreparedApp host, AcceptedExtension extension) =>
            new AppSwitch(host, null, new[] { Switch(host)(extension) }, IfDown.Stand, $"updaemon rollback {extension.Name} {extension.Version}");

        public static AppSwitch Install(PreparedApp host, AcceptedExtension extension, PackageSource source) =>
            new AppSwitch(host, null, new[] { new ExtensionChange.Install(extension, source) }, IfDown.GoBack, $"updaemon install {extension.Name}");

        public static AppSwitch Remove(PreparedApp host, InstalledExtension extension) =>
            new AppSwitch(host, null, new[] { new ExtensionChange.Remove(host.App.NameOf(extension)) }, IfDown.Stand, $"updaemon remove {host.App.NameOf(extension)}");

        /// <summary>The <see cref="RetryCommand"/> of an update of the app.</summary>
        public static string RetryUpdate(string appName) => $"updaemon update {appName}";

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

    /// <summary>What a switch does with one extension.</summary>
    public abstract record ExtensionChange
    {
        private ExtensionChange()
        {
        }

        public abstract ExtensionName Name { get; }

        /// <summary>What becomes current, as its host accepted it; null when nothing does.</summary>
        public abstract AcceptedExtension? To { get; }

        /// <summary>What the host's entry is like once it stands.</summary>
        /// <param name="from">What was current of it.</param>
        internal abstract void Record(InstalledApp host, Version? from);

        public sealed record Install(AcceptedExtension Accepted, PackageSource Source) : ExtensionChange
        {
            public override ExtensionName Name => Accepted.Name;

            public override AcceptedExtension? To => Accepted;

            internal override void Record(InstalledApp host, Version? from) => host.Extensions.Add(new InstalledExtension { Id = Name.Id, Source = Source });
        }

        public sealed record Switch(AcceptedExtension Accepted) : ExtensionChange
        {
            public override ExtensionName Name => Accepted.Name;

            public override AcceptedExtension? To => Accepted;

            internal override void Record(InstalledApp host, Version? from)
            {
                if (from != null)
                {
                    host.Find(Name)!.RecordSwitch(from, Accepted.Version);
                }
            }
        }

        public sealed record Remove(ExtensionName Extension) : ExtensionChange
        {
            public override ExtensionName Name => Extension;

            public override AcceptedExtension? To => null;

            internal override void Record(InstalledApp host, Version? from) => host.Extensions.Remove(host.FindExtension(Name.Id)!);
        }
    }

    /// <summary>One package of an <see cref="AppSwitch"/>, with what was current of it.</summary>
    /// <param name="From">Null for an extension the switch installs.</param>
    /// <param name="To">Null for one it removes.</param>
    public sealed record PackageSwitch(PackageName Name, Version? From, Version? To);
}
