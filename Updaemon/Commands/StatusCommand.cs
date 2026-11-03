using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'status' command: updaemon itself, then one line per app.
    /// </summary>
    public class StatusCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IServiceManager _serviceManager;
        private readonly ITimerManager _timerManager;
        private readonly IOutputWriter _outputWriter;
        private readonly UpdaemonScope _scope;
        private readonly UpdaemonVersion _updaemonVersion;

        public StatusCommand(
            IConfigManager configManager,
            IServiceDeployer serviceDeployer,
            IServiceManager serviceManager,
            ITimerManager timerManager,
            IOutputWriter outputWriter,
            UpdaemonScope scope,
            UpdaemonVersion updaemonVersion)
        {
            _configManager = configManager;
            _serviceDeployer = serviceDeployer;
            _serviceManager = serviceManager;
            _timerManager = timerManager;
            _outputWriter = outputWriter;
            _scope = scope;
            _updaemonVersion = updaemonVersion;
        }

        public string Name => "status";

        public string Description => "Show installed apps and updaemon's own state";

        public string Usage => "updaemon status [app]";

        public bool ChangesState => false;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args);
            if (parser.GetUnknownFlags() is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            IReadOnlyList<InstalledApp>? apps = await AppSelection.SelectAsync(this, parser.Positionals, _configManager, _outputWriter, cancellationToken);
            if (apps == null)
            {
                return 1;
            }

            _outputWriter.WriteLine($"updaemon {_updaemonVersion}, {_scope.Name} scope");
            _outputWriter.WriteLine($"Update timer: {await DescribeTimerAsync(cancellationToken)}");
            UpdaemonConfig config = await _configManager.LoadConfigAsync(cancellationToken);
            _outputWriter.WriteLine($"Self-update: {(config.SelfUpdate ? "on" : "off")}");
            _outputWriter.WriteLine(string.Empty);

            if (apps.Count == 0)
            {
                _outputWriter.WriteLine("No apps installed. Use 'updaemon install <app> --from <plugin>:<remote>' to install one.");
                return 0;
            }

            List<string[]> rows = new List<string[]>();
            foreach (InstalledApp app in apps.OrderBy(app => app.Name, StringComparer.Ordinal))
            {
                rows.Add(await DescribeAsync(app, cancellationToken));
            }

            int[] widths = Enumerable.Range(0, rows[0].Length).Select(column => rows.Max(row => row[column].Length)).ToArray();
            foreach (string[] row in rows)
            {
                _outputWriter.WriteLine(string.Join("  ", row.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd());
            }

            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                Status Command

                Usage:
                  updaemon status [app]

                Description:
                  Shows updaemon's version, its scope (system-wide or this user's) and the
                  update timer, then for each app: its version, what it is, whether its
                  service runs, where it comes from, and any version update skips.

                Examples:
                  updaemon status
                  updaemon status byteshelf
                """;
        }

        private async Task<string> DescribeTimerAsync(CancellationToken cancellationToken)
        {
            if (!await _timerManager.IsTimerEnabledAsync(cancellationToken))
            {
                return "off";
            }

            return await _timerManager.GetCurrentIntervalAsync(cancellationToken) ?? "on (interval unknown)";
        }

        /// <summary>Name, version, kind, run state, source and skipped versions. One app's broken state does not stop the others.</summary>
        private async Task<string[]> DescribeAsync(InstalledApp app, CancellationToken cancellationToken)
        {
            string version;
            try
            {
                version = (await _serviceDeployer.GetCurrentVersionAsync(app.Name, cancellationToken)).ToString();
            }
            catch (InvalidOperationException)
            {
                version = "no version";
            }

            AppManifest? manifest;
            try
            {
                manifest = await _serviceDeployer.GetCurrentManifestAsync(app.Name, cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ManifestException)
            {
                manifest = null;
            }

            string state = manifest?.IsService == true ? await DescribeStateAsync(app.Name, cancellationToken) : "-";

            string skipped = app.SkippedVersions.Count > 0 ? $"skips {string.Join(", ", app.SkippedVersions)}" : string.Empty;
            return new[] { app.Name, version, manifest?.KindLabel ?? "?", state, app.Source, skipped };
        }

        private async Task<string> DescribeStateAsync(string appName, CancellationToken cancellationToken)
        {
            try
            {
                return (await _serviceManager.GetStateAsync(appName, cancellationToken)).IsActive ? "running" : "stopped";
            }
            catch (InvalidOperationException)
            {
                // The service manager could not be asked, for example no user session bus.
                return "?";
            }
        }
    }
}
