using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Switches an installed app's versions and its extensions', in one go and with one restart. The one rule for
    /// a version that failed its health check and for one rolled back from by hand: it is skipped, so update does
    /// not offer it again.
    /// </summary>
    /// <remarks>
    /// Each step is saved before the next (<see cref="AppChange"/>), so a switch that cannot be recorded, on a
    /// full disk say, is put back, and one whose run was killed is put right by the next change to the app (see
    /// <see cref="AppPreparer"/>).
    /// </remarks>
    public class AppSwitcher
    {
        private readonly VersionStore _versions;
        private readonly SwitchReadiness _readiness;
        private readonly SwitchRestorer _restorer;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IServiceManager _serviceManager;
        private readonly IOutputWriter _outputWriter;

        public AppSwitcher(
            VersionStore versions,
            SwitchReadiness readiness,
            SwitchRestorer restorer,
            IServiceDeployer serviceDeployer,
            IServiceManager serviceManager,
            IOutputWriter outputWriter)
        {
            _versions = versions;
            _readiness = readiness;
            _restorer = restorer;
            _serviceDeployer = serviceDeployer;
            _serviceManager = serviceManager;
            _outputWriter = outputWriter;
        }

        /// <summary>
        /// Makes the switch, restarts the service and checks it stays up; when it does not, what becomes of the
        /// switch is its <see cref="AppSwitch.IfDown"/>. When anything else fails before it stands, or the run is
        /// cancelled, everything is put back and nothing is skipped. The service of an app set to manual restart
        /// is left alone, unless the app itself is rolled back by hand, and so is a disabled one when only
        /// extensions change.
        /// </summary>
        /// <exception cref="ServiceManagerUnreachableException">Nothing was written: it cannot be reached.</exception>
        /// <exception cref="InvalidOperationException">Nothing was written, as <see cref="SwitchReadiness"/> says why; or it failed, and the message says whether all is back.</exception>
        public async Task<SwitchOutcome> SwitchAsync(AppSwitch change, CancellationToken cancellationToken = default)
        {
            ReachableServiceManager reachable = await _readiness.MakeReadyAsync(change, cancellationToken);
            RestartDecision restart = await DecideRestartAsync(change, cancellationToken);
            Begun begun = await BeginAsync(change, restart, reachable, cancellationToken);
            return change.IfDown == IfDown.GoBack
                ? await SwitchIfItStaysUpAsync(begun, cancellationToken)
                : await SwitchWhateverItDoesAsync(begun, cancellationToken);
        }

        /// <summary>The switch stands once the service stays up with it.</summary>
        private async Task<SwitchOutcome> SwitchIfItStaysUpAsync(Begun begun, CancellationToken cancellationToken)
        {
            bool restarting = false;
            bool staysUp = true;
            try
            {
                await ApplyAsync(begun, cancellationToken);
                restarting = begun.Restarts;
                if (restarting)
                {
                    staysUp = await _serviceDeployer.RestartAsync(begun.AppName, begun.Reachable, cancellationToken);
                    begun.Entry.ServiceRestarted();
                }

                if (staysUp)
                {
                    await begun.Entry.CommitAsync(left: null, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                await UndoAsync(begun, restarting, ex);
                throw;
            }

            if (!staysUp)
            {
                return await FallBackAsync(begun, cancellationToken);
            }

            await CleanUpAsync(begun, cancellationToken);
            return new SwitchOutcome.Stands(begun.Switched, begun.Restart);
        }

        /// <summary>The switch stands before the service is restarted, and whatever that does with it.</summary>
        private async Task<SwitchOutcome> SwitchWhateverItDoesAsync(Begun begun, CancellationToken cancellationToken)
        {
            try
            {
                await ApplyAsync(begun, cancellationToken);
                await begun.Entry.CommitAsync(begun.Restarts ? new UnfinishedChange.CarryOut { Restart = true } : null, cancellationToken);
            }
            catch (Exception ex)
            {
                await UndoAsync(begun, restarting: false, ex);
                throw;
            }

            await CleanUpAsync(begun, cancellationToken);
            if (!begun.Restarts)
            {
                return new SwitchOutcome.Stands(begun.Switched, begun.Restart);
            }

            try
            {
                await _serviceManager.RestartServiceAsync(begun.AppName, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Still to be restarted: the next change to the app does it.
                return new SwitchOutcome.StandsDown(begun.Switched, begun.Restart, ex.Message);
            }

            begun.Entry.ServiceRestarted();
            await begun.Entry.ClearNoteAsync(_outputWriter);
            return await _serviceDeployer.StaysRunningAsync(begun.AppName, cancellationToken)
                ? new SwitchOutcome.Stands(begun.Switched, begun.Restart)
                : new SwitchOutcome.StandsDown(begun.Switched, begun.Restart, RestartFailure: null);
        }

        /// <summary>Made before anything changes, so a service manager that cannot tell changes nothing.</summary>
        private async Task<RestartDecision> DecideRestartAsync(AppSwitch change, CancellationToken cancellationToken)
        {
            InstalledApp app = change.App;
            bool switchesApp = change.NewVersion != null;
            if (!(change.NewVersion?.Manifest.IsService ?? _serviceDeployer.HasService(app.Name)))
            {
                return RestartDecision.NoService;
            }

            if (switchesApp && change.IfDown == IfDown.Stand)
            {
                return RestartDecision.Restart;
            }

            if (!app.AutoRestart)
            {
                return RestartDecision.Manual;
            }

            return switchesApp || await _serviceManager.IsEnabledAsync(app.Name, cancellationToken) ? RestartDecision.Restart : RestartDecision.Disabled;
        }

        /// <summary>A switch under way.</summary>
        /// <param name="PutBack">What is saved to put back.</param>
        /// <param name="Manifest">That of the app's version that will run; null when the switch does not need it.</param>
        private sealed record Begun(
            AppSwitch Change,
            AppChange Entry,
            IReadOnlyList<PackageSwitch> Switched,
            UnfinishedChange.PutBack PutBack,
            AppManifest? Manifest,
            RestartDecision Restart,
            ReachableServiceManager Reachable)
        {
            public string AppName => Entry.Before.Name;

            public bool Restarts => Restart == RestartDecision.Restart;
        }

        /// <summary>Makes the entry what it is once the switch stands, and saves what to put back until then.</summary>
        private async Task<Begun> BeginAsync(AppSwitch change, RestartDecision restart, ReachableServiceManager reachable, CancellationToken cancellationToken)
        {
            AppChange entry = await change.Prepared.BeginChangeAsync(cancellationToken);
            IReadOnlyList<PackageSwitch> switched = await FindSwitchedAsync(change, cancellationToken);
            AppManifest? manifest = change.OnlyRemoves
                ? null
                : change.NewVersion?.Manifest ?? await _serviceDeployer.GetCurrentManifestAsync(change.App.Name, cancellationToken);
            Record(change, switched, manifest, entry);
            UnfinishedChange.PutBack putBack = new UnfinishedChange.PutBack
            {
                Restart = restart == RestartDecision.Restart,
                Versions = switched.Select(package => new VersionBefore { Extension = (package.Name as ExtensionName)?.Id, Version = package.From }).ToList(),
                NewManifest = change.NewVersion?.Manifest,
                RemovedService = change.NewVersion?.Manifest.IsService == false ? await FindServiceAsync(change.App.Name, cancellationToken) : null,
            };
            await entry.SaveNoteAsync(putBack, cancellationToken);
            return new Begun(change, entry, switched, putBack, manifest, restart, reachable);
        }

        private async Task<IReadOnlyList<PackageSwitch>> FindSwitchedAsync(AppSwitch change, CancellationToken cancellationToken)
        {
            List<PackageSwitch> switched = new List<PackageSwitch>();
            if (change.NewVersion is DeployableVersion next)
            {
                switched.Add(new PackageSwitch(change.App.PackageName, await _versions.GetCurrentAsync(change.App.PackageName, cancellationToken), next.Version));
            }

            foreach (ExtensionChange extension in change.Extensions)
            {
                switched.Add(new PackageSwitch(extension.Name, await _versions.FindCurrentAsync(extension.Name, cancellationToken), extension.To?.Version));
            }

            return switched;
        }

        /// <summary>Makes <paramref name="entry"/> what it is once the switch stands.</summary>
        private static void Record(AppSwitch change, IReadOnlyList<PackageSwitch> switched, AppManifest? manifest, AppChange entry)
        {
            InstalledApp after = entry.After;
            if (change.NewVersion is DeployableVersion next)
            {
                after.RecordSwitch(switched[0].From!, next.Version);
                if (next.ManifestInDirectory)
                {
                    // The release ships its own manifest; one from the install flags is no longer used.
                    after.Manifest = null;
                }

                // A release can newly declare a setting secret; its value then moves out of the readable settings.
                entry.Values.ConformTo(next.Manifest);
            }

            if (manifest != null)
            {
                entry.Values.DropSettingsSetByUpdaemon(manifest);
            }

            foreach (ExtensionChange extension in change.Extensions)
            {
                extension.Record(after, switched.Single(package => package.Name == extension.Name).From);
            }
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

        private async Task ApplyAsync(Begun begun, CancellationToken cancellationToken)
        {
            AppSwitch change = begun.Change;
            await begun.Entry.SaveSecretsAsync(cancellationToken);
            if (change.NewVersion != null)
            {
                await _serviceDeployer.ActivateAsync(begun.Entry.After, change.NewVersion, begun.Reachable, cancellationToken: cancellationToken);
            }
            else if (!change.OnlyRemoves)
            {
                // Needed for a host activated by an updaemon that knew no extensions, without their folder and its
                // variable. For any other it writes the same again.
                await _serviceDeployer.RegenerateAsync(begun.Entry, begun.Reachable, cancellationToken);
            }

            foreach (ExtensionChange extension in change.Extensions)
            {
                if (extension.To is AcceptedExtension to)
                {
                    await _versions.PointAsync(to.Name, to.Version, cancellationToken);
                }
                else
                {
                    _versions.Unlink(extension.Name);
                }
            }
        }

        /// <summary>Once the switch stands, which what fails here does not change.</summary>
        private async Task CleanUpAsync(Begun begun, CancellationToken cancellationToken)
        {
            foreach (PackageSwitch removed in begun.Switched.Where(package => package.To == null))
            {
                _versions.TryDeleteAll(removed.Name);
            }

            if (begun.Manifest == null)
            {
                return;
            }

            try
            {
                // Values stored for what updaemon sets now, from before the app declared it. Only now, as secrets
                // are not put back; their wrapper already overrides them, so one left behind changes nothing.
                begun.Entry.Values.DropSecretsSetByUpdaemon(begun.Manifest);
                await begun.Entry.SaveSecretsAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _outputWriter.WriteError($"Warning: {begun.AppName}: {ex.Message}");
            }
        }

        /// <summary>
        /// After <paramref name="failure"/>, cancelling included: puts everything back and, when the service may
        /// have been restarted, restarts it. Skips nothing, so the next update tries again. Runs to its end.
        /// </summary>
        /// <exception cref="InvalidOperationException">Putting back failed too; the next change to the app tries again.</exception>
        private async Task UndoAsync(Begun begun, bool restarting, Exception failure)
        {
            string? problem;
            try
            {
                await _restorer.RestoreVersionsAsync(begun.Entry.Before, begun.PutBack, begun.Reachable);
                problem = restarting ? await _restorer.RestartAsync(begun.AppName) : null;
                if (problem == null)
                {
                    if (restarting)
                    {
                        begun.Entry.ServiceRestarted();
                    }

                    await begun.Entry.RevertAsync();
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
                _outputWriter.WriteError($"Warning: switching {begun.AppName} back failed: {problem}");
                return;
            }

            throw new InvalidOperationException($"{failure.Message.TrimEnd('.')}, and switching back failed: {problem}", failure);
        }

        /// <summary>
        /// After the service did not stay up with the new versions: makes the ones before current again and
        /// skips the new ones. They stay current even when the skips cannot be recorded, as the new ones are
        /// known not to run.
        /// </summary>
        private async Task<SwitchOutcome> FallBackAsync(Begun begun, CancellationToken cancellationToken)
        {
            string? skipNotRecorded = null;
            bool? staysUp = null;
            try
            {
                await _restorer.RestoreVersionsAsync(begun.Entry.Before, begun.PutBack, begun.Reachable);

                // What is current again may be no service.
                bool hasService = _serviceDeployer.HasService(begun.AppName);
                try
                {
                    await begun.Entry.RevertAsync(entry => Skip(begun.Switched, entry), hasService ? new UnfinishedChange.CarryOut { Restart = true } : null);
                }
                catch (Exception ex)
                {
                    skipNotRecorded = ex.Message;
                }

                if (hasService)
                {
                    await _serviceManager.RestartServiceAsync(begun.AppName, cancellationToken);
                    begun.Entry.ServiceRestarted();
                    if (skipNotRecorded == null)
                    {
                        await begun.Entry.ClearNoteAsync(_outputWriter);
                    }

                    staysUp = await _serviceDeployer.StaysRunningAsync(begun.AppName, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new SwitchOutcome.NotBack(begun.Switched, begun.Restart, ex.Message);
            }

            return new SwitchOutcome.WentBack(begun.Switched, begun.Restart, staysUp, skipNotRecorded);
        }

        /// <summary>None for what the switch installed: it is not in the entry.</summary>
        private static void Skip(IReadOnlyList<PackageSwitch> switched, InstalledApp entry)
        {
            foreach (PackageSwitch package in switched.Where(package => package.From != null && package.To != null))
            {
                entry.Find(package.Name)!.RecordSwitch(from: package.To!, to: package.From!);
            }
        }
    }
}
