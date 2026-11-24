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
        public async Task<SwitchOutcome> SwitchAsync(AppSwitch appSwitch, CancellationToken cancellationToken = default)
        {
            ReachableServiceManager reachable = await _readiness.MakeReadyAsync(appSwitch, cancellationToken);
            RestartDecision restart = await DecideRestartAsync(appSwitch, cancellationToken);
            Begun begun = await BeginAsync(appSwitch, restart, reachable, cancellationToken);
            return appSwitch.IfDown == IfDown.GoBack
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
                    await _serviceDeployer.RestartForAsync(begun.Change, begun.Reachable, cancellationToken);
                    staysUp = await _serviceDeployer.StaysRunningAsync(begun.AppName, cancellationToken);
                }

                if (staysUp)
                {
                    await begun.Change.CommitAsync(left: null, cancellationToken);
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

            await DropSecretsSetByUpdaemonAsync(begun, cancellationToken);
            return begun.Stands();
        }

        /// <summary>The switch stands before the service is restarted, and whatever that does with it.</summary>
        private async Task<SwitchOutcome> SwitchWhateverItDoesAsync(Begun begun, CancellationToken cancellationToken)
        {
            try
            {
                await ApplyAsync(begun, cancellationToken);
                await begun.Change.CommitAsync(begun.Restarts ? new UnfinishedChange.CarryOut { Restart = true } : null, cancellationToken);
            }
            catch (Exception ex)
            {
                await UndoAsync(begun, restarting: false, ex);
                throw;
            }

            await DropSecretsSetByUpdaemonAsync(begun, cancellationToken);
            if (begun.Restarts)
            {
                try
                {
                    await _serviceDeployer.RestartForAsync(begun.Change, begun.Reachable, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Still to be restarted: the next change to the app does it.
                    return new SwitchOutcome.StandsDown(begun.Switched, begun.Restart, ex.Message);
                }
            }

            DeleteRemoved(begun);
            return !begun.Restarts || await _serviceDeployer.StaysRunningAsync(begun.AppName, cancellationToken)
                ? begun.Stands()
                : new SwitchOutcome.StandsDown(begun.Switched, begun.Restart, RestartFailure: null);
        }

        /// <summary>Made before anything changes, so a service manager that cannot tell changes nothing.</summary>
        private async Task<RestartDecision> DecideRestartAsync(AppSwitch appSwitch, CancellationToken cancellationToken)
        {
            InstalledApp app = appSwitch.App;
            bool switchesApp = appSwitch.Next != null;
            if (!(appSwitch.Next?.Version.Manifest.IsService ?? _serviceDeployer.HasService(app.Name)))
            {
                return RestartDecision.NoService;
            }

            if (switchesApp && appSwitch.IfDown == IfDown.Stand)
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
            AppSwitch AppSwitch,
            AppChange Change,
            IReadOnlyList<PackageSwitch> Switched,
            UnfinishedChange.PutBack PutBack,
            AppManifest? Manifest,
            RestartDecision Restart,
            ReachableServiceManager Reachable)
        {
            public string AppName => Change.Before.Name;

            public bool Restarts => Restart == RestartDecision.Restart;

            public SwitchOutcome Stands() => new SwitchOutcome.Stands(Switched, Restart, Change.PrepareNext());
        }

        /// <summary>Makes the entry what it is once the switch stands, and saves what to put back until then.</summary>
        private async Task<Begun> BeginAsync(AppSwitch appSwitch, RestartDecision restart, ReachableServiceManager reachable, CancellationToken cancellationToken)
        {
            AppChange change = await appSwitch.Prepared.BeginChangeAsync(cancellationToken);
            List<PackageSwitch> switched = new List<PackageSwitch>();
            AppBefore? appBefore = null;
            if (appSwitch.Next is NextAppVersion next)
            {
                AppName name = appSwitch.App.PackageName;
                PackageSwitch.Moved app = new PackageSwitch.Moved(name, await _versions.GetCurrentAsync(name, cancellationToken), next.Version.Version);
                switched.Add(app);
                appBefore = new AppBefore
                {
                    Version = app.From,
                    NewManifest = next.Version.Manifest,
                    RemovedService = next.Version.Manifest.IsService ? null : await FindServiceAsync(appSwitch.App.Name, cancellationToken),
                };
            }

            List<ExtensionBefore> extensionsBefore = new List<ExtensionBefore>();
            foreach (ExtensionChange extension in appSwitch.Extensions)
            {
                Version? current = await _versions.FindCurrentAsync(extension.Name, cancellationToken);
                switched.Add(Describe(extension, current));
                extensionsBefore.Add(new ExtensionBefore { Id = extension.Name.Id, Version = current });
            }

            AppManifest? manifest = appSwitch.OnlyRemoves
                ? null
                : appSwitch.Next?.Version.Manifest ?? await _serviceDeployer.GetCurrentManifestAsync(appSwitch.App.Name, cancellationToken);
            Record(appSwitch, switched, manifest, change);
            UnfinishedChange.PutBack putBack = new UnfinishedChange.PutBack { Restart = restart == RestartDecision.Restart, App = appBefore, Extensions = extensionsBefore };
            await change.SaveNoteAsync(putBack, cancellationToken);
            return new Begun(appSwitch, change, switched, putBack, manifest, restart, reachable);
        }

        private static PackageSwitch Describe(ExtensionChange extension, Version? current) => extension switch
        {
            ExtensionChange.Install install => new PackageSwitch.Installed(install.Name, install.Accepted.Version, install.Source),
            ExtensionChange.Switch move => new PackageSwitch.Moved(move.Name, current ?? throw VersionStore.NotDeployed(move.Name), move.Accepted.Version),
            ExtensionChange.Remove remove => new PackageSwitch.Removed(remove.Name),
            _ => throw new InvalidOperationException($"There is no switch for {extension.GetType().Name}."),
        };

        /// <summary>Makes what <paramref name="change"/> saves what it is once the switch stands.</summary>
        private static void Record(AppSwitch appSwitch, IReadOnlyList<PackageSwitch> switched, AppManifest? manifest, AppChange change)
        {
            if (appSwitch.Next?.Version is DeployableVersion next)
            {
                if (next.ManifestInDirectory)
                {
                    // The release ships its own manifest; one from the install flags is no longer used.
                    change.After.Manifest = null;
                }

                // A release can newly declare a setting secret; its value then moves out of the readable settings.
                change.Values.ConformTo(next.Manifest);
            }

            if (manifest != null)
            {
                change.Values.DropSettingsSetByUpdaemon(manifest);
            }

            foreach (PackageSwitch package in switched)
            {
                package.Record(change.After);
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
            AppSwitch appSwitch = begun.AppSwitch;
            await begun.Change.SaveSecretsAsync(cancellationToken);
            if (appSwitch.Next?.Version is DeployableVersion next)
            {
                bool addsService = next.Manifest.IsService && !_serviceDeployer.HasService(begun.AppName);
                await _serviceDeployer.ActivateAsync(begun.Change.After, next, begun.Reachable, cancellationToken: cancellationToken);
                if (addsService)
                {
                    // As installing it would have, so it starts at boot too. Going back disables it with its unit.
                    await _serviceManager.EnableServiceAsync(begun.AppName, cancellationToken);
                }
            }
            else if (!appSwitch.OnlyRemoves)
            {
                // Needed for a host activated by an updaemon that knew no extensions, without their folder and its
                // variable. For any other it writes the same again.
                await _serviceDeployer.RegenerateAsync(begun.Change, begun.Reachable, cancellationToken);
            }

            foreach (ExtensionChange extension in appSwitch.Extensions)
            {
                if (extension.To is AcceptedExtension to)
                {
                    await _versions.PointAsync(to, cancellationToken);
                }
                else
                {
                    _versions.Unlink(extension.Name);
                }
            }
        }

        /// <summary>
        /// Once the service was restarted without them, or is none. One that still runs with them may read their
        /// files: those go with the host, or when the extension is installed again.
        /// </summary>
        private void DeleteRemoved(Begun begun)
        {
            if (begun.Restart is RestartDecision.Restart or RestartDecision.NoService)
            {
                foreach (PackageSwitch.Removed removed in begun.Switched.OfType<PackageSwitch.Removed>())
                {
                    _versions.TryDeleteAll(removed.Name);
                }
            }
        }

        /// <summary>
        /// Drops the secrets stored for what updaemon sets, from before the app declared it. Only once the switch
        /// stands, as secrets are not put back; their wrapper already overrides them, so one left behind changes
        /// nothing, and neither does it that this fails.
        /// </summary>
        private async Task DropSecretsSetByUpdaemonAsync(Begun begun, CancellationToken cancellationToken)
        {
            if (begun.Manifest == null)
            {
                return;
            }

            try
            {
                begun.Change.Values.DropSecretsSetByUpdaemon(begun.Manifest);
                await begun.Change.SaveSecretsAsync(cancellationToken);
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
            string? problem = null;
            try
            {
                await _restorer.RestoreVersionsAsync(begun.Change.Before, begun.PutBack, begun.Reachable);
                // What is current again may be no service.
                if (restarting && _serviceDeployer.HasService(begun.AppName))
                {
                    await _serviceDeployer.RestartForAsync(begun.Change, begun.Reachable, CancellationToken.None);
                }

                await begun.Change.RevertAsync();
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
        /// known not to run. Runs to its end but for the health check, so what failed is told also when the run
        /// is cancelled.
        /// </summary>
        private async Task<SwitchOutcome> FallBackAsync(Begun begun, CancellationToken cancellationToken)
        {
            string? skipNotRecorded = null;
            bool? staysUp = null;
            try
            {
                await _restorer.RestoreVersionsAsync(begun.Change.Before, begun.PutBack, begun.Reachable);

                // What is current again may be no service.
                bool hasService = _serviceDeployer.HasService(begun.AppName);
                try
                {
                    await begun.Change.RevertAsync(entry => Skip(begun.Switched, entry), hasService ? new UnfinishedChange.CarryOut { Restart = true } : null);
                }
                catch (Exception ex)
                {
                    skipNotRecorded = ex.Message;
                }

                if (hasService)
                {
                    await _serviceDeployer.RestartForAsync(begun.Change, begun.Reachable, CancellationToken.None);
                    staysUp = await _serviceDeployer.StaysRunningAsync(begun.AppName, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The run ends where it next looks.
            }
            catch (Exception ex)
            {
                return new SwitchOutcome.NotBack(begun.Switched, begun.Restart, ex.Message);
            }

            return new SwitchOutcome.WentBack(begun.Switched, begun.Restart, staysUp, skipNotRecorded);
        }

        /// <summary>None for what the switch installed: it is not in the entry.</summary>
        private static void Skip(IReadOnlyList<PackageSwitch> switched, InstalledApp entry)
        {
            foreach (PackageSwitch.Moved package in switched.OfType<PackageSwitch.Moved>())
            {
                package.Skip(entry);
            }
        }
    }
}
