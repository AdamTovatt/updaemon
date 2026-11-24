using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'rollback' command: makes an earlier retained version of an app or an extension current again.
    /// </summary>
    public class RollbackCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly VersionStore _versions;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly AppSwitcher _switcher;
        private readonly ExtensionCheck _check;
        private readonly IOutputWriter _outputWriter;

        public RollbackCommand(IConfigManager configManager, VersionStore versions, IServiceDeployer serviceDeployer, AppSwitcher switcher, ExtensionCheck check, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _versions = versions;
            _serviceDeployer = serviceDeployer;
            _switcher = switcher;
            _check = check;
            _outputWriter = outputWriter;
        }

        public string Name => "rollback";

        public string Description => "Go back to an earlier version of an app or extension";

        public string Usage => "updaemon rollback <app>[/<extension>] [version]";

        public bool ChangesState(string[] args) => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args);
            if (parser.GetUnknownFlags() is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            string name;
            Version? named = null;
            switch (parser.Positionals)
            {
                case [string only]:
                    name = only;
                    break;
                case [string first, string version] when Version.TryParse(version.TrimStart('v'), out named):
                    name = first;
                    break;
                default:
                    return _outputWriter.FailUsage(this);
            }

            SelectedPackage? selected = await AppSelection.FindAsync(name, _configManager, _outputWriter, cancellationToken);
            if (selected == null)
            {
                return 1;
            }

            if (await _switcher.PrepareAsync(selected, cancellationToken) is not PreparedApp prepared)
            {
                return _outputWriter.FailNotInstalled(name);
            }

            Version current = await _versions.GetCurrentAsync(selected.Name, cancellationToken);
            IReadOnlyList<Version> kept = _versions.GetRetained(selected.Name);
            Version? target = named != null
                ? kept.FirstOrDefault(version => SameVersion(version, named)) ?? named
                : selected.Package.FindRollbackTarget(current, kept);
            if (target == null)
            {
                return _outputWriter.Fail($"No version of {selected.Name} older than {current} is kept, apart from skipped ones. Name one to roll back to it anyway.");
            }

            if (target == current)
            {
                return _outputWriter.Fail($"{selected.Name} {current} is already the current version.");
            }

            return selected.Extension == null
                ? await RollBackAppAsync(prepared, current, target, cancellationToken)
                : await RollBackExtensionAsync(prepared, selected.Extension, current, target, cancellationToken);
        }

        /// <summary>Its extensions stay as they are: the version rolled back to leaves out those it cannot load.</summary>
        private async Task<int> RollBackAppAsync(PreparedApp prepared, Version current, Version target, CancellationToken cancellationToken)
        {
            InstalledApp app = prepared.App;
            DeployableVersion opened = await _serviceDeployer.OpenRetainedAsync(app.Name, target, cancellationToken);
            AppSwitch change = AppSwitch.ToVersion(prepared, opened);
            await _switcher.MakeReadyToRollBackAsync(change, cancellationToken);
            foreach (InstalledExtension extension in app.Extensions)
            {
                await WarnOfRefusalAsync(app, opened, app.NameOf(extension), cancellationToken);
            }

            return await RollBackAsync(change, app.PackageName, current, target, cancellationToken);
        }

        private async Task WarnOfRefusalAsync(InstalledApp app, DeployableVersion target, ExtensionName name, CancellationToken cancellationToken)
        {
            try
            {
                Version version = await _versions.GetCurrentAsync(name, cancellationToken);
                if (await _check.FindRefusalAsync(app, target, name, version, cancellationToken) is ExtensionRefusal refusal)
                {
                    _outputWriter.WriteError($"Warning: {name}: {refusal}. {app.Name} runs without it.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The rollback does not depend on it.
                _outputWriter.WriteError($"Warning: {name}: {ex.Message}");
            }
        }

        private async Task<int> RollBackExtensionAsync(PreparedApp prepared, InstalledExtension extension, Version current, Version target, CancellationToken cancellationToken)
        {
            InstalledApp host = prepared.App;
            ExtensionName name = host.NameOf(extension);
            DeployableVersion hostVersion = await _serviceDeployer.OpenCurrentAsync(host.Name, cancellationToken);
            return await _check.FindRefusalAsync(host, hostVersion, name, target, cancellationToken) is ExtensionRefusal refusal
                ? _outputWriter.Fail($"{name} was not rolled back: {refusal}.")
                : await RollBackAsync(AppSwitch.OfExtension(prepared, extension, target), name, current, target, cancellationToken);
        }

        private async Task<int> RollBackAsync(AppSwitch change, PackageName name, Version current, Version target, CancellationToken cancellationToken)
        {
            string? problem = await _switcher.RollBackAsync(change, cancellationToken);
            _outputWriter.WriteLine(target < current
                ? $"Rolled back {name} from {current} to {target}. 'updaemon update' skips {current} from now on."
                : $"Switched {name} from {current} to {target}.");
            return problem == null ? 0 : _outputWriter.Fail($"{problem}. {ServiceDeployer.SeeLogs(change.App.Name)}");
        }

        /// <summary>"1.2" names the version kept as "1.2.0": missing parts count as zero.</summary>
        private static bool SameVersion(Version kept, Version named)
        {
            return kept.Major == named.Major
                && kept.Minor == named.Minor
                && Math.Max(kept.Build, 0) == Math.Max(named.Build, 0)
                && Math.Max(kept.Revision, 0) == Math.Max(named.Revision, 0);
        }

        public string GetDetailedHelp()
        {
            return """
                Rollback Command

                Usage:
                  updaemon rollback <app>[/<extension>] [version]

                Description:
                  Makes an earlier version current again: the newest kept version older than
                  the current one that is not skipped, or the named kept version, even a
                  skipped one. Rebuilds the app's command and service from that version's
                  updaemon.json and restarts the service. The version rolled back from is
                  skipped: 'updaemon update' does not install it again, only a newer release.
                  Naming a newer kept version switches forward to it, for example to undo a
                  rollback; that skips nothing.

                  An app's extensions stay at their versions; one the version rolled back
                  to refuses is named, and that version runs without it. A version that takes
                  no extensions at all is refused while any is installed.

                  <app>/<extension> goes back to an earlier version of that extension, if
                  the app accepts it, and restarts the app unless it is not a service, is
                  set to manual restart or is disabled.

                Examples:
                  updaemon rollback byteshelf
                  updaemon rollback byteshelf 1.2.0
                  updaemon rollback byteshelf/images
                """;
        }
    }
}
