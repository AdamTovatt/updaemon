using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Every switch of an installed app's versions, its own and its extensions': makes them current in one
    /// go, restarts the service once, and puts everything back when that fails. The one rule for a version
    /// that failed its health check and for one rolled back from by hand: it is skipped, so update does not
    /// offer it again.
    /// </summary>
    /// <remarks>
    /// A switch first saves in the app's entry what to put back (<see cref="UnfinishedSwitch"/>), and ends by
    /// saving the entry without it. So a switch that cannot be recorded, on a full disk say, is put back, and
    /// one whose run was killed is put back by the next change to the app, in <see cref="PrepareAsync"/>.
    /// </remarks>
    public class AppSwitcher
    {
        private readonly VersionStore _versions;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IServiceManager _serviceManager;
        private readonly AppSettingsStore _settingsStore;
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;

        public AppSwitcher(
            VersionStore versions,
            IServiceDeployer serviceDeployer,
            IServiceManager serviceManager,
            AppSettingsStore settingsStore,
            IConfigManager configManager,
            IOutputWriter outputWriter)
        {
            _versions = versions;
            _serviceDeployer = serviceDeployer;
            _serviceManager = serviceManager;
            _settingsStore = settingsStore;
            _configManager = configManager;
            _outputWriter = outputWriter;
        }

        /// <summary>
        /// The first step of any command that switches an installed app's versions or applies its settings,
        /// before it writes anything: ends a switch that a killed run left unfinished. What was current then
        /// is current again, and <paramref name="app"/> says so; no version is skipped, as nothing proved one
        /// bad. Needs neither the network nor a plugin.
        /// </summary>
        /// <exception cref="ServiceManagerUnreachableException">There is a switch to end, and the service manager it needs cannot be reached.</exception>
        /// <exception cref="InvalidOperationException">What was current cannot be made current again.</exception>
        public async Task<PreparedApp> PrepareAsync(InstalledApp app, CancellationToken cancellationToken = default)
        {
            if (app.Unfinished is not UnfinishedSwitch unfinished)
            {
                return new PreparedApp(app);
            }

            // A service the switch removed is not there to see now, but is to be put back.
            ReachableServiceManager reachable = unfinished.RemovedService != null
                ? await _serviceManager.EnsureReachableAsync(cancellationToken)
                : await _serviceDeployer.ReachServiceManagerAsync(app.Name, cancellationToken: cancellationToken);
            string before = string.Join(", ", unfinished.PutBack.Select(version => Describe(version, app)));
            _outputWriter.WriteLine(unfinished.PutBack.Count == 0
                ? $"{app.Name}: an earlier run was cut off before it restarted the service; restarting it."
                : $"{app.Name}: an earlier run was cut off while it switched versions; putting back {before}.");
            try
            {
                await RestoreVersionsAsync(app, reachable);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"putting back {before} failed: {ex.Message} Until that works {app.Name} cannot be changed; 'updaemon remove {app.Name}' still removes it.", ex);
            }

            if (await RestartUnfinishedAsync(app) is string problem)
            {
                _outputWriter.WriteError($"Warning: restarting {app.Name} failed: {problem}");
            }

            await EndAsync(app);
            return new PreparedApp(app);
        }

        /// <summary>
        /// Switches, restarts the service and checks it stays up; when it does not, everything is put back,
        /// the versions switched to are skipped and an extension the switch installs is gone again. When
        /// anything else fails, or the run is cancelled, everything is put back too, and nothing is skipped.
        /// The service of an app set to manual restart is left alone, and so is a disabled one when only
        /// extensions change.
        /// </summary>
        /// <exception cref="ServiceManagerUnreachableException">Nothing was written: it cannot be reached.</exception>
        /// <exception cref="InvalidOperationException">Nothing was written: the app's new version cannot take over (see <see cref="EnsureCanTakeOverAsync"/>).</exception>
        /// <exception cref="NotRunningException">The service did not stay up; the message says what runs now.</exception>
        public async Task UpdateAsync(AppSwitch change, CancellationToken cancellationToken = default)
        {
            InstalledApp app = change.App;
            ReachableServiceManager reachable = await _serviceDeployer.ReachServiceManagerAsync(app.Name, change.NewVersion?.Manifest, cancellationToken);
            await EnsureCanTakeOverAsync(change, $"updaemon update {app.Name}", cancellationToken);
            RestartDecision restart = await DecideRestartAsync(change, byHand: false, cancellationToken);
            IReadOnlyList<PackageSwitch> switched = await BeginAsync(change, restart, cancellationToken);
            bool restarting = false;
            bool staysRunning = true;
            try
            {
                await ApplyAsync(change, reachable, cancellationToken);
                if (restart == RestartDecision.Restart)
                {
                    restarting = true;
                    staysRunning = await _serviceDeployer.RestartAsync(app.Name, reachable, cancellationToken);
                }

                if (staysRunning)
                {
                    await RecordAsync(change, switched, unfinished: null, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                await UndoAsync(app.Name, restarting, reachable, ex);
                throw;
            }

            if (!staysRunning)
            {
                throw await FallBackAsync(app, switched, reachable, cancellationToken);
            }

            await DropValuesSetByUpdaemonAsync(app, cancellationToken);
            ReportRestart(change, switched, restart);
        }

        /// <summary>
        /// Switches to kept versions by hand: that stands even when the service then does not stay up. A
        /// rolled back app's service is always restarted; for an extension, as in <see cref="UpdateAsync"/>.
        /// A switch that cannot be recorded is put back.
        /// </summary>
        /// <returns>What is wrong with the service after its restart, as a sentence without its period; null when nothing is.</returns>
        /// <exception cref="ServiceManagerUnreachableException">Nothing was written: it cannot be reached.</exception>
        /// <exception cref="InvalidOperationException">Nothing was written: the app's version cannot take over, or its executable is gone.</exception>
        public async Task<string?> RollBackAsync(AppSwitch change, CancellationToken cancellationToken = default)
        {
            InstalledApp app = change.App;
            ReachableServiceManager reachable = await _serviceDeployer.ReachServiceManagerAsync(app.Name, change.NewVersion?.Manifest, cancellationToken);
            if (change.NewVersion is DeployableVersion version)
            {
                await _serviceDeployer.MakeExecutableAsync(app.Name, version, cancellationToken);
                await EnsureCanTakeOverAsync(change, $"updaemon rollback {app.Name} {version.Version}", cancellationToken);
            }

            return await SwitchByHandAsync(change, reachable, cancellationToken);
        }

        /// <summary>
        /// Takes the extension out of its host, deletes its versions and restarts the host as
        /// <see cref="UpdateAsync"/> does. The removal stands even when the host then does not stay up; one
        /// that cannot be recorded is put back.
        /// </summary>
        /// <returns>As <see cref="RollBackAsync"/>.</returns>
        /// <exception cref="ServiceManagerUnreachableException">Nothing was written: it cannot be reached.</exception>
        public async Task<string?> RemoveExtensionAsync(PreparedApp host, InstalledExtension extension, CancellationToken cancellationToken = default)
        {
            ReachableServiceManager reachable = await _serviceDeployer.ReachServiceManagerAsync(host.App.Name, cancellationToken: cancellationToken);
            return await SwitchByHandAsync(AppSwitch.OfExtension(host, extension, to: null), reachable, cancellationToken);
        }

        /// <summary>
        /// Throws when the app's new version needs a setting that has no value, or no longer takes the
        /// extensions installed into the app. The message says what to run before <paramref name="retryCommand"/>.
        /// </summary>
        private async Task EnsureCanTakeOverAsync(AppSwitch change, string retryCommand, CancellationToken cancellationToken)
        {
            if (change.NewVersion is not DeployableVersion next)
            {
                return;
            }

            InstalledApp app = change.App;
            Version running = await _versions.GetCurrentAsync(app.PackageName, cancellationToken);
            AppValues values = await _settingsStore.LoadValuesAsync(app, cancellationToken);
            values.EnsureComplete(next.Manifest, next.Version, running, retryCommand);
            if (next.Manifest.Extensions == null && app.Extensions.Count > 0)
            {
                string removals = string.Join(", ", app.Extensions.Select(extension => $"'updaemon remove {app.NameOf(extension)}'"));
                throw new InvalidOperationException($"{next.Version} takes no extensions; {running} keeps running. Run {removals}, then '{retryCommand}'.");
            }
        }

        /// <summary>Whether a switch restarts the service, or why not.</summary>
        private enum RestartDecision
        {
            Restart,
            NoService,
            Manual,
            Disabled,
        }

        /// <summary>Made before anything changes, so a service manager that cannot tell changes nothing.</summary>
        private async Task<RestartDecision> DecideRestartAsync(AppSwitch change, bool byHand, CancellationToken cancellationToken)
        {
            InstalledApp app = change.App;
            bool switchesApp = change.NewVersion != null;
            if (!(change.NewVersion?.Manifest.IsService ?? _serviceDeployer.HasService(app.Name)))
            {
                return RestartDecision.NoService;
            }

            if (switchesApp && byHand)
            {
                return RestartDecision.Restart;
            }

            if (!app.AutoRestart)
            {
                return RestartDecision.Manual;
            }

            return switchesApp || await _serviceManager.IsEnabledAsync(app.Name, cancellationToken) ? RestartDecision.Restart : RestartDecision.Disabled;
        }

        private void ReportRestart(AppSwitch change, IReadOnlyList<PackageSwitch> switched, RestartDecision restart)
        {
            string name = change.App.Name;
            if (restart == RestartDecision.Manual)
            {
                string running = switched.FirstOrDefault(package => package.Name is AppName)?.From!.ToString() ?? "the extensions it started with";
                _outputWriter.WriteLine($"{name}: restart skipped (restart=manual); the service runs {running} until it restarts.");
            }
            else if (restart == RestartDecision.Disabled)
            {
                _outputWriter.WriteLine($"{name}: restart skipped, as its service is disabled.");
            }
        }

        /// <summary>Saves what to put back, with the values the new version moves into the secrets.</summary>
        private async Task<IReadOnlyList<PackageSwitch>> BeginAsync(AppSwitch change, RestartDecision restart, CancellationToken cancellationToken)
        {
            InstalledApp app = change.App;
            UnfinishedSwitch unfinished = new UnfinishedSwitch { Restart = restart == RestartDecision.Restart };
            List<PackageSwitch> switched = new List<PackageSwitch>();
            AppValues values = await _settingsStore.LoadValuesAsync(app, cancellationToken);
            if (change.NewVersion != null)
            {
                Version from = await _versions.GetCurrentAsync(app.PackageName, cancellationToken);
                switched.Add(new PackageSwitch(app.PackageName, app, from, change.NewVersion.Version));
                unfinished.PutBack.Add(new VersionBefore { Version = from });
                unfinished.RemovedService = change.NewVersion.Manifest.IsService ? null : await FindServiceAsync(app.Name, cancellationToken);

                // A release can newly declare a setting secret; its value then moves out of the readable settings.
                values.ConformTo(change.NewVersion.Manifest);
            }

            foreach (ExtensionSwitch extension in change.Extensions)
            {
                ExtensionName name = app.NameOf(extension.Extension);
                Version? from = await _versions.FindCurrentAsync(name, cancellationToken);
                switched.Add(new PackageSwitch(name, extension.Extension, from, extension.To));
                unfinished.PutBack.Add(new VersionBefore { Extension = extension.Extension.Id, Version = from });
                if (!app.Extensions.Contains(extension.Extension))
                {
                    app.Extensions.Add(extension.Extension);
                }
            }

            app.Unfinished = unfinished;
            await _settingsStore.SaveAsync(values, cancellationToken);
            return switched;
        }

        private async Task<ServiceBefore?> FindServiceAsync(string appName, CancellationToken cancellationToken)
        {
            return !_serviceDeployer.HasService(appName)
                ? null
                : new ServiceBefore
                {
                    Enabled = await _serviceManager.IsEnabledAsync(appName, cancellationToken),
                    Running = (await _serviceManager.GetStateAsync(appName, cancellationToken)).IsActive,
                };
        }

        private async Task ApplyAsync(AppSwitch change, ReachableServiceManager reachable, CancellationToken cancellationToken)
        {
            if (change.NewVersion != null)
            {
                await _serviceDeployer.ActivateAsync(change.App, change.NewVersion, reachable, cancellationToken);
            }
            else if (change.Extensions.Any(extension => extension.To != null))
            {
                // The host may have been activated by an updaemon that knew no extensions, without their folder and its variable.
                await _serviceDeployer.RegenerateAsync(change.App, reachable, cancellationToken);
            }

            foreach (ExtensionSwitch extension in change.Extensions)
            {
                ExtensionName name = change.App.NameOf(extension.Extension);
                if (extension.To == null)
                {
                    _versions.Unlink(name);
                }
                else
                {
                    await _versions.PointAsync(name, extension.To, cancellationToken);
                }
            }
        }

        /// <summary>Saves the entry as the switch leaves it: its skips, its extensions, and what is left to do.</summary>
        private async Task RecordAsync(AppSwitch change, IReadOnlyList<PackageSwitch> switched, UnfinishedSwitch? unfinished, CancellationToken cancellationToken)
        {
            InstalledApp app = change.App;
            if (change.NewVersion?.ManifestInDirectory == true)
            {
                // The release ships its own manifest; one from the install flags is no longer used.
                app.Manifest = null;
            }

            foreach (PackageSwitch package in switched)
            {
                package.Record();
            }

            app.Extensions.RemoveAll(extension => change.Extensions.Any(removed => removed.To == null && removed.Extension == extension));
            app.Unfinished = unfinished;
            await _configManager.SaveAppAsync(app, cancellationToken);
        }

        /// <summary>
        /// Once the switch stands: drops values stored for what updaemon sets now, from before the app declared
        /// it. Their wrapper already overrides them, so one left behind by a failure here changes nothing.
        /// </summary>
        private async Task DropValuesSetByUpdaemonAsync(InstalledApp app, CancellationToken cancellationToken)
        {
            try
            {
                AppValues values = await _settingsStore.LoadValuesAsync(app, cancellationToken);
                if (values.DropSetByUpdaemon(await _serviceDeployer.GetCurrentManifestAsync(app.Name, cancellationToken)))
                {
                    await _settingsStore.SaveAsync(values, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _outputWriter.WriteError($"Warning: {app.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// A switch that stands whatever the service does next: makes it and records it, or puts everything
        /// back; then restarts the service and checks it. Returns what <see cref="RollBackAsync"/> does.
        /// </summary>
        private async Task<string?> SwitchByHandAsync(AppSwitch change, ReachableServiceManager reachable, CancellationToken cancellationToken)
        {
            InstalledApp app = change.App;
            RestartDecision restart = await DecideRestartAsync(change, byHand: true, cancellationToken);
            IReadOnlyList<PackageSwitch> switched = await BeginAsync(change, restart, cancellationToken);
            try
            {
                await ApplyAsync(change, reachable, cancellationToken);
                await RecordAsync(change, switched, restart == RestartDecision.Restart ? new UnfinishedSwitch { Restart = true } : null, cancellationToken);
            }
            catch (Exception ex)
            {
                await UndoAsync(app.Name, restarting: false, reachable, ex);
                throw;
            }

            foreach (PackageSwitch removed in switched.Where(package => package.To == null))
            {
                DeleteVersions(removed.Name);
            }

            await DropValuesSetByUpdaemonAsync(app, cancellationToken);
            ReportRestart(change, switched, restart);
            return restart == RestartDecision.Restart ? await RestartByHandAsync(app, cancellationToken) : null;
        }

        private async Task<string?> RestartByHandAsync(InstalledApp app, CancellationToken cancellationToken)
        {
            try
            {
                await _serviceManager.RestartServiceAsync(app.Name, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Still to be restarted: the next change to the app does it.
                return $"{app.Name} could not be restarted: {ex.Message.TrimEnd('.')}";
            }

            try
            {
                // Before the health check: cancelling that leaves nothing to make up for.
                await EndAsync(app);
            }
            catch (Exception ex)
            {
                _outputWriter.WriteError($"Warning: the next change to {app.Name} restarts it once more: {ex.Message}");
            }

            return await _serviceDeployer.StaysRunningAsync(app.Name, cancellationToken) ? null : ServiceDeployer.NotRunningAfterRestart(app.Name);
        }

        /// <summary>
        /// After <paramref name="failure"/>, cancelling included: puts everything back and, when the service may
        /// have been restarted, restarts it. Skips nothing, so the next update tries again. Goes by what is
        /// saved, which a failed write leaves as it was, and runs to its end.
        /// </summary>
        /// <exception cref="InvalidOperationException">Putting back failed too; the next change to the app tries again.</exception>
        private async Task UndoAsync(string appName, bool restarting, ReachableServiceManager reachable, Exception failure)
        {
            string? problem = null;
            try
            {
                InstalledApp? app = await _configManager.GetAppAsync(appName, CancellationToken.None);
                if (app?.Unfinished == null)
                {
                    return;
                }

                app.Unfinished.Restart &= restarting;
                await RestoreVersionsAsync(app, reachable);
                problem = await RestartUnfinishedAsync(app);
                if (problem == null)
                {
                    await EndAsync(app);
                }
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }

            if (problem == null)
            {
                return;
            }

            if (failure is OperationCanceledException)
            {
                _outputWriter.WriteError($"Warning: switching {appName} back failed: {problem}");
                return;
            }

            throw new InvalidOperationException($"{failure.Message.TrimEnd('.')}, and switching back failed: {problem}", failure);
        }

        /// <summary>
        /// After the service did not stay up with the new versions: makes the ones before current again and
        /// skips the new ones. They stay current even when the skips cannot be recorded, as the new ones are
        /// known not to run.
        /// </summary>
        private async Task<NotRunningException> FallBackAsync(InstalledApp app, IReadOnlyList<PackageSwitch> switched, ReachableServiceManager reachable, CancellationToken cancellationToken)
        {
            SwitchText text = new SwitchText(app.Name, switched);
            string? skipNotRecorded = null;
            bool? staysRunning = null;
            try
            {
                await RestoreVersionsAsync(app, reachable);
                foreach (PackageSwitch package in switched)
                {
                    package.RecordGoingBack();
                }

                // What is current again may be no service.
                bool hasService = _serviceDeployer.HasService(app.Name);
                app.Unfinished = hasService ? new UnfinishedSwitch { Restart = true } : null;
                try
                {
                    await _configManager.SaveAppAsync(app, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    skipNotRecorded = ex.Message;
                }

                if (hasService)
                {
                    await _serviceManager.RestartServiceAsync(app.Name, cancellationToken);
                    if (skipNotRecorded == null)
                    {
                        // Before the health check: cancelling that leaves nothing to make up for.
                        await EndAsync(app);
                    }

                    staysRunning = await _serviceDeployer.StaysRunningAsync(app.Name, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new NotRunningException(text.PuttingBackFailed(ex.Message));
            }

            return new NotRunningException(text.PutBack(staysRunning, skipNotRecorded));
        }

        /// <summary>
        /// Makes what <see cref="UnfinishedSwitch.PutBack"/> names current again, all of it even when one fails,
        /// and takes out an extension that was not there before. Does not restart and does not save.
        /// </summary>
        /// <exception cref="InvalidOperationException">Some of it could not be; the message says what.</exception>
        private async Task RestoreVersionsAsync(InstalledApp app, ReachableServiceManager reachable)
        {
            List<string> problems = new List<string>();
            foreach (VersionBefore before in app.Unfinished!.PutBack)
            {
                try
                {
                    await RestoreVersionAsync(app, before, reachable);
                }
                catch (Exception ex)
                {
                    problems.Add(ex.Message.TrimEnd('.'));
                }
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(string.Join("; ", problems) + ".");
            }
        }

        private async Task RestoreVersionAsync(InstalledApp app, VersionBefore before, ReachableServiceManager reachable)
        {
            if (before.Extension == null)
            {
                DeployableVersion version = await _serviceDeployer.OpenRetainedAsync(app.Name, before.Version!, CancellationToken.None);
                await _serviceDeployer.ActivateAsync(app, version, reachable, CancellationToken.None);
                if (app.Unfinished!.RemovedService is ServiceBefore service)
                {
                    await RestoreServiceAsync(app.Name, service);
                }

                return;
            }

            PackageName name = before.NameIn(app);
            if (before.Version != null)
            {
                await _versions.PointAsync(name, before.Version, CancellationToken.None);
                return;
            }

            _versions.Unlink(name);
            app.Extensions.RemoveAll(extension => extension.Id == before.Extension);
            DeleteVersions(name);
        }

        /// <summary>Activating a version that is no service stopped and removed this one, and activating its version only wrote its unit again.</summary>
        private async Task RestoreServiceAsync(string appName, ServiceBefore before)
        {
            if (before.Enabled && !await _serviceManager.IsEnabledAsync(appName, CancellationToken.None))
            {
                await _serviceManager.EnableServiceAsync(appName, CancellationToken.None);
            }

            if (before.Running && !(await _serviceManager.GetStateAsync(appName, CancellationToken.None)).IsActive)
            {
                await _serviceManager.StartServiceAsync(appName, CancellationToken.None);
            }
        }

        /// <summary>Restarts the service if the unfinished switch does and it still is one, without a health check. Returns what failed, or null.</summary>
        private async Task<string?> RestartUnfinishedAsync(InstalledApp app)
        {
            if (!app.Unfinished!.Restart || !_serviceDeployer.HasService(app.Name))
            {
                return null;
            }

            try
            {
                await _serviceManager.RestartServiceAsync(app.Name, CancellationToken.None);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private async Task EndAsync(InstalledApp app)
        {
            app.Unfinished = null;
            await _configManager.SaveAppAsync(app, CancellationToken.None);
        }

        /// <summary>Best effort: what is left is not linked, and goes with the host or when it is installed again.</summary>
        private void DeleteVersions(PackageName name)
        {
            try
            {
                _versions.DeleteAll(name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _outputWriter.WriteError($"Warning: could not delete the versions of {name}: {ex.Message}");
            }
        }

        private static string Describe(VersionBefore before, InstalledApp app) =>
            before.Version == null ? $"no {before.NameIn(app)}" : $"{before.NameIn(app)} {before.Version}";
    }
}
