using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Updates installed apps and their extensions. For each app, everything new is downloaded first, from
    /// whichever plugins; then the app's version that will run is asked about each extension; then what can
    /// be switched is, in one go, with one restart (<see cref="AppSwitcher"/>). One package that
    /// cannot be updated does not stop the others.
    /// </summary>
    public class AppUpdater
    {
        private readonly IConfigManager _configManager;
        private readonly PluginSessions _sessions;
        private readonly VersionStore _versions;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly AppPreparer _preparer;
        private readonly SwitchReadiness _readiness;
        private readonly AppSwitcher _switcher;
        private readonly ExtensionCheck _check;
        private readonly IOutputWriter _outputWriter;

        public AppUpdater(
            IConfigManager configManager,
            PluginSessions sessions,
            VersionStore versions,
            IServiceDeployer serviceDeployer,
            AppPreparer preparer,
            SwitchReadiness readiness,
            AppSwitcher switcher,
            ExtensionCheck check,
            IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _sessions = sessions;
            _versions = versions;
            _serviceDeployer = serviceDeployer;
            _preparer = preparer;
            _readiness = readiness;
            _switcher = switcher;
            _check = check;
            _outputWriter = outputWriter;
        }

        /// <summary>Updates each app with its extensions, and each extension by itself. What fails goes to <paramref name="failures"/>.</summary>
        /// <param name="retentionCount">How many versions of each package to keep.</param>
        /// <returns>The names of all that did not fail.</returns>
        public async Task<IReadOnlyList<string>> RunAsync(
            IReadOnlyList<PreparedPackage> selected, int retentionCount, UpdateFailures failures, CancellationToken cancellationToken = default)
        {
            List<Target> targets = new List<Target>();
            foreach (PreparedPackage package in selected)
            {
                if (await FindTargetAsync(package, failures, cancellationToken) is Target target)
                {
                    targets.Add(target);
                }
            }

            List<SelectedPackage> packages = targets.SelectMany(target => target.Packages).ToList();
            await using (PluginSession session = _sessions.Open())
            {
                Run run = new Run(session, packages, retentionCount, failures);
                foreach (Target target in targets)
                {
                    await UpdateAsync(target, run, cancellationToken);
                }
            }

            return packages.Select(package => package.Name.ToString()).Except(failures.All.Select(failure => failure.Subject)).ToList();
        }

        /// <summary>What to update of one app together; the app itself, if it is, comes first.</summary>
        private sealed record Target(PreparedApp Prepared, IReadOnlyList<SelectedPackage> Packages)
        {
            public InstalledApp App => Prepared.App;
        }

        private sealed class Run
        {
            /// <summary>Those a plugin that does not start is reported for: all of its, but those that failed before.</summary>
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

            public void Report(SelectedPackage package, string reason)
            {
                _waiting.Remove(package);
                Failures.Report(package.Name.ToString(), reason);
            }

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

        /// <summary>What there is to update. Null when that failed, which is reported.</summary>
        private async Task<Target?> FindTargetAsync(PreparedPackage selected, UpdateFailures failures, CancellationToken cancellationToken)
        {
            try
            {
                InstalledApp app = selected.Prepared.App;
                await _versions.GetCurrentAsync(app.PackageName, cancellationToken);
                List<SelectedPackage> packages = new List<SelectedPackage>();
                IEnumerable<SelectedPackage> extensions = app.Extensions.Select(extension => new SelectedPackage(app, extension));
                foreach (SelectedPackage package in selected.Extension == null ? extensions.Prepend(selected.Package) : new[] { selected.Package })
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

                return packages.Count == 0 ? null : new Target(selected.Prepared, packages);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Report(selected.Package.Name.ToString(), ex.Message);
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
                    run.Failures.Report(change == null ? new[] { target.Packages[0].Name.ToString() } : Names(change), ex.Message);
                }
            }
        }

        private static string[] Names(AppSwitch change) => change.Names.Select(name => name.ToString()).ToArray();

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
            foreach (SelectedPackage package in target.Packages)
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
                run.Report(selected, ex.Message);
            }

            return null;
        }

        /// <summary>What to switch to of what was downloaded; null when nothing.</summary>
        private async Task<AppSwitch?> ChooseAsync(Target target, StagedVersions staged, Run run, CancellationToken cancellationToken)
        {
            DeployableVersion? newHost = staged.Host != null && await CanTakeOverAsync(target.App, staged.Host, run, cancellationToken) ? staged.Host : null;
            IReadOnlyList<AcceptedExtension> extensions = await AskAsync(target, newHost, staged.Extensions, run, cancellationToken);
            return (newHost, extensions.Count) switch
            {
                (null, 0) => null,
                (null, _) => AppSwitch.UpdateExtensions(target.Prepared, extensions),
                _ => AppSwitch.Update(target.Prepared, newHost, extensions),
            };
        }

        private async Task SwitchAsync(AppSwitch change, StagedVersions staged, Run run, CancellationToken cancellationToken)
        {
            string app = change.App.Name;
            SwitchOutcome outcome = await _switcher.SwitchAsync(change, cancellationToken);
            // Also those that do not stand: though skipped, so they can be rolled forward to by name.
            staged.Keep(change);
            if (SwitchText.Problem(app, outcome) is string problem)
            {
                run.Failures.Report(Names(change), problem);
                return;
            }

            _outputWriter.WriteRestartSkipped(app, outcome);
            foreach (PackageSwitch package in outcome.Switched)
            {
                _outputWriter.WriteLine($"Updated {package.Name} from {package.From} to {package.To}.");
            }

            await CleanUpAsync(app, outcome.Switched, run.RetentionCount, cancellationToken);
        }

        /// <summary>Whether the app can be updated; when not, which is reported, its extensions still are.</summary>
        private async Task<bool> CanTakeOverAsync(InstalledApp app, DeployableVersion next, Run run, CancellationToken cancellationToken)
        {
            try
            {
                await _readiness.MakeReadyAsync(app, next, AppSwitch.RetryUpdate(app.Name), cancellationToken);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                run.Failures.Report(app.Name, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Asks the version of the host that will run about each extension, also one without anything new:
        /// one it does not load is reported by every run. Returns the new versions it would load.
        /// </summary>
        private async Task<List<AcceptedExtension>> AskAsync(
            Target target, DeployableVersion? newHost, IReadOnlyDictionary<InstalledExtension, Version> newExtensions, Run run, CancellationToken cancellationToken)
        {
            List<AcceptedExtension> accepted = new List<AcceptedExtension>();
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
                    ExtensionVerdict? ofStaged = staged == null ? null : await _check.AskAsync(app, host, app.NameOf(extension), staged, cancellationToken);
                    if (ofStaged is AcceptedExtension newVersion)
                    {
                        accepted.Add(newVersion);
                    }
                    else if (await FindProblemAsync(app, host, extension, ofStaged as ExtensionRefusal, cancellationToken) is string problem)
                    {
                        run.Failures.Report(name, problem);
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
        /// <param name="ofStaged">Its refusal of a new version of it; null when there is none, so the current one stays.</param>
        private async Task<string?> FindProblemAsync(InstalledApp app, DeployableVersion host, InstalledExtension extension, ExtensionRefusal? ofStaged, CancellationToken cancellationToken)
        {
            ExtensionName name = app.NameOf(extension);
            Version current = await _versions.GetCurrentAsync(name, cancellationToken);
            ExtensionRefusal? ofCurrent = await _check.AskAsync(app, host, name, current, cancellationToken) as ExtensionRefusal;
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
        private async Task CleanUpAsync(string appName, IReadOnlyList<PackageSwitch> switched, int retentionCount, CancellationToken cancellationToken)
        {
            try
            {
                InstalledApp app = (await _configManager.GetAppAsync(appName, cancellationToken))!;
                AppChange change = await (await _preparer.PrepareAsync(app, cancellationToken)).BeginChangeAsync(cancellationToken);
                bool forgot = false;
                foreach (PackageSwitch package in switched)
                {
                    await _versions.PruneAsync(package.Name, retentionCount, cancellationToken);
                    forgot |= change.After.Find(package.Name)!.ForgetSkipsOlderThan(package.To!, _versions.GetRetained(package.Name));
                }

                if (forgot)
                {
                    await change.CommitAsync(left: null, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _outputWriter.WriteError($"Warning: {appName}: failed to clean up after the update: {ex.Message}");
            }
        }
    }
}
