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
        private readonly SwitchRestorer _restorer;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IServiceManager _serviceManager;
        private readonly AppSettingsStore _settingsStore;
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;

        public AppSwitcher(
            VersionStore versions,
            SwitchRestorer restorer,
            IServiceDeployer serviceDeployer,
            IServiceManager serviceManager,
            AppSettingsStore settingsStore,
            IConfigManager configManager,
            IOutputWriter outputWriter)
        {
            _versions = versions;
            _restorer = restorer;
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
                await _restorer.RestoreVersionsAsync(app, reachable);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"putting back {before} failed: {ex.Message} Until that works {app.Name} cannot be changed; 'updaemon remove {app.Name}' still removes it.", ex);
            }

            if (await _restorer.RestartAsync(app) is string problem)
            {
                _outputWriter.WriteError($"Warning: restarting {app.Name} failed: {problem}");
            }

            await ClearUnfinishedAsync(app);
            return new PreparedApp(app);
        }

        /// <summary>The first step of one that works on an installed app or extension, as above.</summary>
        /// <returns>Null when it was an extension that a killed run was installing: it is taken out again.</returns>
        public async Task<PreparedApp?> PrepareAsync(SelectedPackage package, CancellationToken cancellationToken = default)
        {
            PreparedApp prepared = await PrepareAsync(package.App, cancellationToken);
            return package.Extension == null || prepared.App.Extensions.Contains(package.Extension) ? prepared : null;
        }

        /// <summary>
        /// Switches, restarts the service and checks it stays up; when it does not, everything is put back,
        /// the versions switched to are skipped and an extension the switch installs is gone again. When
        /// anything else fails, or the run is cancelled, everything is put back too, and nothing is skipped.
        /// The service of an app set to manual restart is left alone, and so is a disabled one when only
        /// extensions change.
        /// </summary>
        /// <exception cref="ServiceManagerUnreachableException">Nothing was written: it cannot be reached.</exception>
        /// <exception cref="InvalidOperationException">Nothing was written: the app's new version cannot take over (see <see cref="EnsureCanUpdateAsync"/>).</exception>
        /// <exception cref="NotRunningException">The service did not stay up; the message says what runs now.</exception>
        /// <returns>What was switched.</returns>
        public async Task<IReadOnlyList<PackageSwitch>> UpdateAsync(AppSwitch change, CancellationToken cancellationToken = default)
        {
            InstalledApp app = change.App;
            ReachableServiceManager reachable = await EnsureCanUpdateAsync(change, cancellationToken);
            RestartDecision restart = await DecideRestartAsync(app, change.NewVersion, byHand: false, cancellationToken);
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
                    Record(change, switched);
                    await SaveAsync(app, unfinished: null, cancellationToken);
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
            ReportRestart(app.Name, switched, restart);
            return switched;
        }

        /// <summary>
        /// Throws when <see cref="UpdateAsync"/> would before it writes anything: the service manager cannot be
        /// reached, or the app's new version needs a setting that has no value or no longer takes the extensions
        /// installed into the app.
        /// </summary>
        /// <remarks>
        /// Not for a command name that is taken: that fails the switch, its extensions' with it, which goes
        /// back and is tried again by the next run.
        /// </remarks>
        /// <exception cref="ServiceManagerUnreachableException">It cannot be reached.</exception>
        /// <exception cref="InvalidOperationException">The app's new version cannot take over; the message says what to run first.</exception>
        public async Task<ReachableServiceManager> EnsureCanUpdateAsync(AppSwitch change, CancellationToken cancellationToken = default)
        {
            ReachableServiceManager reachable = await _serviceDeployer.ReachServiceManagerAsync(change.App.Name, change.NewVersion?.Manifest, cancellationToken);
            await EnsureCanTakeOverAsync(change, $"updaemon update {change.App.Name}", cancellationToken);
            return reachable;
        }

        /// <summary>
        /// Switches to kept versions by hand: that stands even when the service then does not stay up. A
        /// rolled back app's service is always restarted; for an extension, as in <see cref="UpdateAsync"/>.
        /// A switch that cannot be recorded is put back.
        /// </summary>
        /// <returns>What is wrong with the service after its restart, as a sentence without its period; null when nothing is.</returns>
        /// <exception cref="ServiceManagerUnreachableException">Nothing was written: it cannot be reached.</exception>
        /// <exception cref="InvalidOperationException">Nothing was written: the app's version cannot take over, its executable is gone, or an extension is not installed.</exception>
        public async Task<string?> RollBackAsync(AppSwitch change, CancellationToken cancellationToken = default)
        {
            InstalledApp app = change.App;
            ReachableServiceManager reachable = await MakeReadyToRollBackAsync(change, cancellationToken);
            RestartDecision restart = await DecideRestartAsync(app, change.NewVersion, byHand: true, cancellationToken);
            IReadOnlyList<PackageSwitch> switched = await BeginAsync(change, restart, cancellationToken);
            await MakeStandAsync(
                app,
                restart,
                reachable,
                async () =>
                {
                    await ApplyAsync(change, reachable, cancellationToken);
                    Record(change, switched);
                },
                cancellationToken);
            return await FinishByHandAsync(app, switched, restart, cancellationToken);
        }

        /// <summary>Makes the app's version ready to run, as it is asked about its extensions before it is switched to, and throws when <see cref="RollBackAsync"/> would before it writes anything.</summary>
        /// <exception cref="ServiceManagerUnreachableException">It cannot be reached.</exception>
        /// <exception cref="InvalidOperationException">The app's version cannot take over, its executable is gone, or an extension is not installed.</exception>
        public async Task<ReachableServiceManager> MakeReadyToRollBackAsync(AppSwitch change, CancellationToken cancellationToken = default)
        {
            if (change.Extensions.FirstOrDefault(extension => !change.App.Extensions.Contains(extension.Extension)) is ExtensionSwitch unknown)
            {
                throw new InvalidOperationException($"{change.App.NameOf(unknown.Extension)} is not installed.");
            }

            ReachableServiceManager reachable = await _serviceDeployer.ReachServiceManagerAsync(change.App.Name, change.NewVersion?.Manifest, cancellationToken);
            if (change.NewVersion is DeployableVersion version)
            {
                await _serviceDeployer.MakeExecutableAsync(change.App.Name, version, cancellationToken);
                await EnsureCanTakeOverAsync(change, $"updaemon rollback {change.App.Name} {version.Version}", cancellationToken);
            }

            return reachable;
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
            InstalledApp app = host.App;
            ExtensionName name = app.NameOf(extension);
            ReachableServiceManager reachable = await _serviceDeployer.ReachServiceManagerAsync(app.Name, cancellationToken: cancellationToken);
            RestartDecision restart = await DecideRestartAsync(app, newVersion: null, byHand: true, cancellationToken);
            VersionBefore before = new VersionBefore { Extension = extension.Id, Version = await _versions.FindCurrentAsync(name, cancellationToken) };
            host.BeginChange();
            await SaveAsync(app, new UnfinishedSwitch { Restart = restart == RestartDecision.Restart, PutBack = { before } }, cancellationToken);
            await MakeStandAsync(
                app,
                restart,
                reachable,
                () =>
                {
                    _versions.Unlink(name);
                    app.Extensions.Remove(extension);
                    return Task.CompletedTask;
                },
                cancellationToken);
            _versions.TryDeleteAll(name);
            return await FinishByHandAsync(app, Array.Empty<PackageSwitch>(), restart, cancellationToken);
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
        private async Task<RestartDecision> DecideRestartAsync(InstalledApp app, DeployableVersion? newVersion, bool byHand, CancellationToken cancellationToken)
        {
            bool switchesApp = newVersion != null;
            if (!(newVersion?.Manifest.IsService ?? _serviceDeployer.HasService(app.Name)))
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

        private void ReportRestart(string name, IReadOnlyList<PackageSwitch> switched, RestartDecision restart)
        {
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
            change.Prepared.BeginChange();
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
            else
            {
                // Needed for a host activated by an updaemon that knew no extensions, without their folder and its
                // variable. For any other it writes the same again.
                await _serviceDeployer.RegenerateAsync(change.Prepared, reachable, cancellationToken);
            }

            foreach (ExtensionSwitch extension in change.Extensions)
            {
                await _versions.PointAsync(change.App.NameOf(extension.Extension), extension.To, cancellationToken);
            }
        }

        /// <summary>What the entry is like once the switch stands.</summary>
        private static void Record(AppSwitch change, IReadOnlyList<PackageSwitch> switched)
        {
            if (change.NewVersion?.ManifestInDirectory == true)
            {
                // The release ships its own manifest; one from the install flags is no longer used.
                change.App.Manifest = null;
            }

            foreach (PackageSwitch package in switched)
            {
                package.Record();
            }
        }

        /// <summary>Nothing is left to make up for.</summary>
        private Task ClearUnfinishedAsync(InstalledApp app) => SaveAsync(app, unfinished: null, CancellationToken.None);

        /// <summary>When it fails, what is unfinished stays so in <paramref name="app"/> too.</summary>
        private async Task SaveAsync(InstalledApp app, UnfinishedSwitch? unfinished, CancellationToken cancellationToken)
        {
            UnfinishedSwitch? saved = app.Unfinished;
            app.Unfinished = unfinished;
            try
            {
                await _configManager.SaveAppAsync(app, cancellationToken);
            }
            catch
            {
                app.Unfinished = saved;
                throw;
            }
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

        /// <summary>The part of a change by hand that stands whatever the service does next: makes and saves it, or puts everything back.</summary>
        private async Task MakeStandAsync(InstalledApp app, RestartDecision restart, ReachableServiceManager reachable, Func<Task> make, CancellationToken cancellationToken)
        {
            try
            {
                await make();
                await SaveAsync(app, restart == RestartDecision.Restart ? new UnfinishedSwitch { Restart = true } : null, cancellationToken);
            }
            catch (Exception ex)
            {
                await UndoAsync(app.Name, restarting: false, reachable, ex);
                throw;
            }
        }

        /// <summary>What is left once it stands: the restart and its check. Returns what <see cref="RollBackAsync"/> does.</summary>
        private async Task<string?> FinishByHandAsync(InstalledApp app, IReadOnlyList<PackageSwitch> switched, RestartDecision restart, CancellationToken cancellationToken)
        {
            await DropValuesSetByUpdaemonAsync(app, cancellationToken);
            ReportRestart(app.Name, switched, restart);
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
                await ClearUnfinishedAsync(app);
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
                await _restorer.RestoreVersionsAsync(app, reachable);
                problem = await _restorer.RestartAsync(app);
                if (problem == null)
                {
                    await ClearUnfinishedAsync(app);
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
                await _restorer.RestoreVersionsAsync(app, reachable);
                foreach (PackageSwitch package in switched)
                {
                    package.RecordGoingBack();
                }

                // What is current again may be no service.
                bool hasService = _serviceDeployer.HasService(app.Name);
                try
                {
                    await SaveAsync(app, hasService ? new UnfinishedSwitch { Restart = true } : null, CancellationToken.None);
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
                        await ClearUnfinishedAsync(app);
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

        private static string Describe(VersionBefore before, InstalledApp app) =>
            before.Version == null ? $"no {before.NameIn(app)}" : $"{before.NameIn(app)} {before.Version}";
    }
}
