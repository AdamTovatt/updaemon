using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Updates installed apps and their extensions. For each app, everything new is downloaded first, from
    /// whichever plugins; then the app's version that will run is asked about each extension; then what can
    /// be switched is, in one go, with one restart (<see cref="AppSwitcher.UpdateAsync"/>). One package that
    /// cannot be updated does not stop the others.
    /// </summary>
    public class AppUpdater
    {
        private readonly IConfigManager _configManager;
        private readonly PluginSessions _sessions;
        private readonly VersionStore _versions;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly AppSwitcher _switcher;
        private readonly ExtensionCheck _check;
        private readonly IOutputWriter _outputWriter;

        public AppUpdater(
            IConfigManager configManager,
            PluginSessions sessions,
            VersionStore versions,
            IServiceDeployer serviceDeployer,
            AppSwitcher switcher,
            ExtensionCheck check,
            IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _sessions = sessions;
            _versions = versions;
            _serviceDeployer = serviceDeployer;
            _switcher = switcher;
            _check = check;
            _outputWriter = outputWriter;
        }

        /// <summary>Updates each app with its extensions, and each extension by itself. What fails goes to <paramref name="failures"/>.</summary>
        /// <param name="retentionCount">How many versions of each package to keep.</param>
        /// <returns>The names of all that did not fail.</returns>
        public async Task<IReadOnlyList<string>> RunAsync(
            IReadOnlyList<SelectedPackage> selected, int retentionCount, UpdateFailures failures, CancellationToken cancellationToken = default)
        {
            List<Target> targets = new List<Target>();
            foreach (SelectedPackage package in selected)
            {
                if (await PrepareAsync(package, failures, cancellationToken) is Target target)
                {
                    targets.Add(target);
                }
            }

            List<SelectedPackage> packages = targets.SelectMany(target => target.Packages).ToList();
            await using (PluginSession session = _sessions.Open())
            {
                Run run = new Run(session, packages, retentionCount, failures);
                foreach (Target target in ByPlugin(targets, target => target.Packages[0]))
                {
                    await UpdateAsync(target, run, cancellationToken);
                }
            }

            return packages.Select(package => package.Name.ToString()).Except(failures.All.Select(failure => failure.Subject)).ToList();
        }

        /// <summary>Those from one plugin after each other, so it is started once; otherwise in their order.</summary>
        private static IEnumerable<T> ByPlugin<T>(IEnumerable<T> items, Func<T, SelectedPackage> package) =>
            items.GroupBy(item => package(item).Package.Plugin).SelectMany(group => group);

        /// <summary>What to update of one app together; the app itself, if it is, comes first.</summary>
        private sealed record Target(PreparedApp Prepared, IReadOnlyList<SelectedPackage> Packages)
        {
            public InstalledApp App => Prepared.App;
        }

        private sealed class Run
        {
            /// <summary>Those whose plugin was not called yet.</summary>
            private readonly List<SelectedPackage> _waiting;

            public Run(PluginSession session, IEnumerable<SelectedPackage> packages, int retentionCount, UpdateFailures failures)
            {
                Session = session;
                _waiting = packages.ToList();
                RetentionCount = retentionCount;
                Failures = failures;
            }

            public PluginSession Session { get; }

            public int RetentionCount { get; }

            public UpdateFailures Failures { get; }

            public void Called(SelectedPackage package) => _waiting.Remove(package);

            /// <summary>Reported for every package still waiting for the plugin, so it is not once more when their turn comes.</summary>
            public void ReportUnavailable(string plugin, string reason)
            {
                string[] waiting = _waiting.Where(package => package.Package.Plugin == plugin).Select(package => package.Name.ToString()).ToArray();
                _waiting.RemoveAll(package => package.Package.Plugin == plugin);
                if (waiting.Length > 0)
                {
                    Failures.Report(waiting, reason);
                }
            }
        }

        /// <summary>Ends what a killed run left, then says what there is to update. Null when that failed, which is reported.</summary>
        private async Task<Target?> PrepareAsync(SelectedPackage selected, UpdateFailures failures, CancellationToken cancellationToken)
        {
            InstalledApp app = selected.App;
            try
            {
                if (await _switcher.PrepareAsync(selected, cancellationToken) is not PreparedApp prepared)
                {
                    failures.Report(selected.Name.ToString(), "it is not installed; the run that was installing it was cut off.");
                    return null;
                }

                await _versions.GetCurrentAsync(app.PackageName, cancellationToken);
                List<SelectedPackage> packages = new List<SelectedPackage>();
                IEnumerable<SelectedPackage> extensions = app.Extensions.Select(extension => new SelectedPackage(app, extension));
                foreach (SelectedPackage package in selected.Extension == null ? extensions.Prepend(selected) : new[] { selected })
                {
                    if (package.Extension == null || await _versions.FindCurrentAsync(package.Name, cancellationToken) != null)
                    {
                        packages.Add(package);
                    }
                    else
                    {
                        failures.Report(package.Name.ToString(), VersionStore.NotDeployed(package.Name).Message);
                    }
                }

                return packages.Count == 0 ? null : new Target(prepared, packages);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Report(selected.Name.ToString(), ex.Message);
            }

            return null;
        }

        private async Task UpdateAsync(Target target, Run run, CancellationToken cancellationToken)
        {
            AppSwitch? change = null;
            await using (StagedVersions staged = new StagedVersions(_versions))
            {
                try
                {
                    await StageAllAsync(target, staged, run, cancellationToken);
                    change = await ChooseAsync(target, staged, run, cancellationToken);
                    if (change != null)
                    {
                        await SwitchAsync(change, staged, run, cancellationToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    run.Failures.Report((change?.Names ?? new[] { target.Packages[0].Name }).Select(name => name.ToString()).ToArray(), ex.Message);
                }
            }
        }

        /// <summary>What was downloaded for one target. Disposing deletes it again, except what was kept, whatever failed: a full disk included.</summary>
        private sealed class StagedVersions : IAsyncDisposable
        {
            private readonly VersionStore _versions;
            private readonly Dictionary<PackageName, Version> _unused = new Dictionary<PackageName, Version>();

            public StagedVersions(VersionStore versions)
            {
                _versions = versions;
            }

            /// <summary>The app's, when there is a new one.</summary>
            public DeployableVersion? Host { get; private set; }

            public Dictionary<InstalledExtension, Version> Extensions { get; } = new Dictionary<InstalledExtension, Version>();

            public void Add(SelectedPackage package, DeployableVersion host)
            {
                Host = host;
                _unused[package.Name] = host.Version;
            }

            public void Add(SelectedPackage package, InstalledExtension extension, Version version)
            {
                Extensions[extension] = version;
                _unused[package.Name] = version;
            }

            public void Keep(AppSwitch change)
            {
                foreach (PackageName name in change.Names)
                {
                    _unused.Remove(name);
                }
            }

            public async ValueTask DisposeAsync()
            {
                foreach ((PackageName name, Version version) in _unused)
                {
                    await _versions.DiscardAsync(name, version);
                }
            }
        }

        private async Task StageAllAsync(Target target, StagedVersions staged, Run run, CancellationToken cancellationToken)
        {
            foreach (SelectedPackage package in ByPlugin(target.Packages, package => package))
            {
                if (package.Extension is InstalledExtension extension)
                {
                    if (await StageAsync(package, run, (version, client) => _versions.DownloadAsync(package.Name, extension, version, client, cancellationToken), cancellationToken) is Version version)
                    {
                        staged.Add(package, extension, version);
                    }
                }
                else if (await StageAsync(package, run, (version, client) => _serviceDeployer.StageAsync(target.App, version, client, cancellationToken), cancellationToken) is DeployableVersion host)
                {
                    staged.Add(package, host);
                }
            }
        }

        /// <summary>Downloads the package's latest release if it is a newer one to update to. Null when not, or when that failed, which is reported.</summary>
        private async Task<T?> StageAsync<T>(SelectedPackage selected, Run run, Func<Version, IDistributionServiceClient, Task<T>> download, CancellationToken cancellationToken)
            where T : class
        {
            (PackageName name, InstalledPackage package) = (selected.Name, selected.Package);
            try
            {
                return await run.Session.RunAsync(
                    package.Plugin,
                    async client =>
                    {
                        run.Called(selected);
                        Version current = await _versions.GetCurrentAsync(name, cancellationToken);
                        Version? latest = await client.GetLatestVersionAsync(package.Remote, cancellationToken);
                        if (latest == null)
                        {
                            _outputWriter.WriteLine($"{name}: no released version found for {package.Source}.");
                            return null;
                        }

                        if (latest <= current)
                        {
                            _outputWriter.WriteLine($"{name} is up to date ({current}).");
                            return null;
                        }

                        if (package.IsSkipped(latest))
                        {
                            _outputWriter.WriteLine($"{name}: {latest} is skipped (it failed its health check or was rolled back from); {current} stays until a newer version is released.");
                            return null;
                        }

                        return await download(latest, client);
                    },
                    cancellationToken);
            }
            catch (PluginUnavailableException ex)
            {
                run.ReportUnavailable(package.Plugin, ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                run.Called(selected);
                run.Failures.Report(name.ToString(), ex.Message);
            }

            return null;
        }

        /// <summary>What to switch to of what was downloaded; null when nothing.</summary>
        private async Task<AppSwitch?> ChooseAsync(Target target, StagedVersions staged, Run run, CancellationToken cancellationToken)
        {
            DeployableVersion? newHost = staged.Host != null && await CanTakeOverAsync(target.Prepared, staged.Host, run, cancellationToken) ? staged.Host : null;
            return AppSwitch.OfAll(target.Prepared, newHost, await AskAsync(target, newHost, staged.Extensions, run, cancellationToken));
        }

        private async Task SwitchAsync(AppSwitch change, StagedVersions staged, Run run, CancellationToken cancellationToken)
        {
            IReadOnlyList<PackageSwitch> switched;
            try
            {
                switched = await _switcher.UpdateAsync(change, cancellationToken);
            }
            catch (NotRunningException)
            {
                // Though skipped, so they can be rolled forward to by name.
                staged.Keep(change);
                throw;
            }

            staged.Keep(change);
            foreach (PackageSwitch package in switched)
            {
                _outputWriter.WriteLine($"Updated {package.Name} from {package.From} to {package.To}.");
            }

            await CleanUpAsync(change.App, switched, run.RetentionCount, cancellationToken);
        }

        /// <summary>Whether the app can be updated; when not, which is reported, its extensions still are.</summary>
        private async Task<bool> CanTakeOverAsync(PreparedApp app, DeployableVersion next, Run run, CancellationToken cancellationToken)
        {
            try
            {
                await _switcher.EnsureCanUpdateAsync(AppSwitch.ToVersion(app, next), cancellationToken);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                run.Failures.Report(app.App.Name, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Asks the version of the host that will run about each extension, also one without anything new:
        /// one it does not load is reported by every run. Returns the new versions it would load.
        /// </summary>
        private async Task<List<ExtensionSwitch>> AskAsync(
            Target target, DeployableVersion? newHost, IReadOnlyDictionary<InstalledExtension, Version> newExtensions, Run run, CancellationToken cancellationToken)
        {
            List<ExtensionSwitch> accepted = new List<ExtensionSwitch>();
            List<InstalledExtension> extensions = target.Packages.Select(package => package.Extension).OfType<InstalledExtension>().ToList();
            if (extensions.Count == 0)
            {
                return accepted;
            }

            InstalledApp app = target.App;
            DeployableVersion host = newHost ?? await _serviceDeployer.OpenCurrentAsync(app.Name, cancellationToken);
            foreach (InstalledExtension extension in extensions)
            {
                string name = app.NameOf(extension).ToString();
                Version? staged = newExtensions.GetValueOrDefault(extension);
                try
                {
                    if (await FindProblemAsync(app, host, extension, staged, cancellationToken) is string problem)
                    {
                        run.Failures.Report(name, problem);
                    }
                    else if (staged != null)
                    {
                        accepted.Add(new ExtensionSwitch(extension, staged));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    run.Failures.Report(name, ex.Message);
                }
            }

            return accepted;
        }

        /// <summary>Why <paramref name="host"/> would not load the extension's newest version; null when it would.</summary>
        /// <param name="staged">A new version of it; null when the current one stays.</param>
        private async Task<string?> FindProblemAsync(InstalledApp app, DeployableVersion host, InstalledExtension extension, Version? staged, CancellationToken cancellationToken)
        {
            ExtensionName name = app.NameOf(extension);
            ExtensionRefusal? ofStaged = staged == null ? null : await _check.FindRefusalAsync(app, host, name, staged, cancellationToken);
            if (staged != null && ofStaged == null)
            {
                return null;
            }

            Version current = await _versions.GetCurrentAsync(name, cancellationToken);
            ExtensionRefusal? ofCurrent = await _check.FindRefusalAsync(app, host, name, current, cancellationToken);
            string tryNext = $"Until a newer release of either fixes that, or 'updaemon remove {name}' removes it, {app.Name} runs without it";
            return (ofStaged, ofCurrent) switch
            {
                (null, null) => null,
                (null, _) => $"{ofCurrent}. {tryNext}.",
                (_, null) => $"{ofStaged}. {current} stays.",
                _ => $"{ofStaged}. It refuses {current} too, which stays: {ofCurrent.Reason}. {tryNext}.",
            };
        }

        /// <summary>After a switch that stands: deletes old versions and forgets the skips that no longer matter. A failure is only a warning.</summary>
        private async Task CleanUpAsync(InstalledApp app, IReadOnlyList<PackageSwitch> switched, int retentionCount, CancellationToken cancellationToken)
        {
            try
            {
                bool forgot = false;
                foreach (PackageSwitch package in switched)
                {
                    await _versions.PruneAsync(package.Name, retentionCount, cancellationToken);
                    forgot |= package.Package.ForgetSkipsOlderThan(package.To, _versions.GetRetained(package.Name));
                }

                if (forgot)
                {
                    await _configManager.SaveAppAsync(app, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _outputWriter.WriteError($"Warning: {app.Name}: failed to clean up after the update: {ex.Message}");
            }
        }
    }
}
