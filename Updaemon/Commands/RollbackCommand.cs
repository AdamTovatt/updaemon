using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'rollback' command: makes an earlier retained version current again.
    /// </summary>
    public class RollbackCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly AppRollback _appRollback;
        private readonly IOutputWriter _outputWriter;

        public RollbackCommand(IConfigManager configManager, IServiceDeployer serviceDeployer, AppRollback appRollback, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _serviceDeployer = serviceDeployer;
            _appRollback = appRollback;
            _outputWriter = outputWriter;
        }

        public string Name => "rollback";

        public string Description => "Go back to an earlier version of an app";

        public string Usage => "updaemon rollback <app> [version]";

        public bool ChangesState => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args);
            if (parser.GetUnknownFlags() is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            string appName;
            Version? named = null;
            switch (parser.Positionals)
            {
                case [string name]:
                    appName = name;
                    break;
                case [string name, string version] when Version.TryParse(version.TrimStart('v'), out named):
                    appName = name;
                    break;
                default:
                    return _outputWriter.FailUsage(this);
            }

            InstalledApp? app = await _configManager.GetAppAsync(appName, cancellationToken);
            if (app == null)
            {
                return _outputWriter.Fail($"'{appName}' is not installed.");
            }

            ReachableServiceManager reachable = await _serviceDeployer.ReachServiceManagerAsync(appName, cancellationToken: cancellationToken);
            Version current = await _serviceDeployer.GetCurrentVersionAsync(appName, cancellationToken);
            IReadOnlyList<Version> kept = _serviceDeployer.GetRetainedVersions(appName);
            Version? target = named != null
                ? kept.FirstOrDefault(version => SameVersion(version, named)) ?? named
                : app.FindRollbackTarget(current, kept);
            if (target == null)
            {
                return _outputWriter.Fail($"No version of {appName} older than {current} is kept, apart from skipped ones. Name one to roll back to it anyway.");
            }

            if (target == current)
            {
                return _outputWriter.Fail($"{appName} {current} is already the current version.");
            }

            await _appRollback.SwitchAsync(app, current, target, reachable, cancellationToken);
            AppManifest manifest = await _serviceDeployer.GetCurrentManifestAsync(appName, cancellationToken);
            bool staysRunning = !manifest.IsService || await _serviceDeployer.RestartAsync(appName, reachable, cancellationToken);

            _outputWriter.WriteLine(target < current
                ? $"Rolled back {appName} from {current} to {target}. 'updaemon update' skips {current} from now on."
                : $"Switched {appName} from {current} to {target}.");
            return staysRunning ? 0 : _outputWriter.Fail($"{appName}: {ServiceDeployer.NotRunning(appName, target)}");
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
                  updaemon rollback <app> [version]

                Description:
                  Makes an earlier version current again: the newest kept version older than
                  the current one that is not skipped, or the named kept version, even a
                  skipped one. Rebuilds the app's command and service from that version's
                  updaemon.json and restarts the service. The version rolled back from is
                  skipped: 'updaemon update' does not install it again, only a newer release.
                  Naming a newer kept version switches forward to it, for example to undo a
                  rollback; that skips nothing.

                Examples:
                  updaemon rollback byteshelf
                  updaemon rollback byteshelf 1.2.0
                """;
        }
    }
}
