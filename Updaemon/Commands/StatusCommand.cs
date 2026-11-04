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
        private readonly UpdateRunLog _runLog;
        private readonly TimeProvider _time;

        public StatusCommand(
            IConfigManager configManager,
            IServiceDeployer serviceDeployer,
            IServiceManager serviceManager,
            ITimerManager timerManager,
            IOutputWriter outputWriter,
            UpdaemonScope scope,
            UpdaemonVersion updaemonVersion,
            UpdateRunLog runLog,
            TimeProvider time)
        {
            _configManager = configManager;
            _serviceDeployer = serviceDeployer;
            _serviceManager = serviceManager;
            _timerManager = timerManager;
            _outputWriter = outputWriter;
            _scope = scope;
            _updaemonVersion = updaemonVersion;
            _runLog = runLog;
            _time = time;
        }

        public string Name => "status";

        public string Description => "Show installed apps and updaemon's own state";

        public string Usage => "updaemon status [app]";

        public bool ChangesState(string[] args) => false;

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
            _outputWriter.WriteLine($"Last update: {await DescribeLastRunAsync(cancellationToken)}");
            // Only a service stops at logout; with none in this scope, there is nothing to warn about.
            IReadOnlyList<InstalledApp> allApps = await _configManager.GetAllAppsAsync(cancellationToken);
            if (allApps.Any(app => _serviceDeployer.HasService(app.Name)))
            {
                await LogoutWarning.WriteIfNeededAsync(_serviceManager, _outputWriter, cancellationToken);
            }

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
                  Shows updaemon's version, its scope (system-wide or this user's), the
                  update timer and the last update run without an app named (when, whether
                  the timer or a person started it, and what failed), then for each app: its
                  version, what it is, whether its service runs, where it comes from, and any
                  version update skips. On Linux, for your own services, it warns when
                  lingering is off, since they then stop when you log out.

                Examples:
                  updaemon status
                  updaemon status byteshelf
                """;
        }

        private async Task<string> DescribeLastRunAsync(CancellationToken cancellationToken)
        {
            try
            {
                return DescribeRun(await _runLog.LoadAsync(cancellationToken), _time.GetUtcNow());
            }
            catch (InvalidOperationException ex)
            {
                return $"unknown ({ex.Message})";
            }
        }

        /// <summary>"2h ago by the timer, 1 failed (byteshelf: ...)", each reason to its first sentence; or "never".</summary>
        internal static string DescribeRun(UpdateRun? run, DateTimeOffset now)
        {
            if (run == null)
            {
                return "never";
            }

            string started = run.StartedBy == UpdateStarter.Timer ? "by the timer" : "by hand";
            string outcome = run.Failures.Count == 0
                ? "nothing failed"
                : $"{run.Failures.Count} failed ({string.Join("; ", run.Failures.Select(failure => $"{failure.Subject}: {FirstSentence(failure.Reason)}"))})";
            return $"{Ago(now - run.FinishedAt)} {started}, {outcome}";
        }

        private static string Ago(TimeSpan elapsed) => elapsed switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes}m ago",
            { TotalDays: < 1 } => $"{(int)elapsed.TotalHours}h ago",
            _ => $"{(int)elapsed.TotalDays}d ago",
        };

        private static string FirstSentence(string reason)
        {
            string line = reason.Split('\n')[0];
            int end = line.IndexOf(". ", StringComparison.Ordinal);
            return (end < 0 ? line : line[..end]).TrimEnd('.');
        }

        private async Task<string> DescribeTimerAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await TimerState.ReadAsync(_timerManager, cancellationToken) switch
                {
                    TimerState.On { Interval: TimeSpan interval } => TimerInterval.Format(interval),
                    TimerState.On => "on (interval unknown)",
                    _ => "off",
                };
            }
            catch (InvalidOperationException ex)
            {
                // Without a reachable service manager, say so rather than guess; the rest still shows.
                return $"unknown ({ex.Message})";
            }
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

            string state = _serviceDeployer.HasService(app.Name) ? await DescribeStateAsync(app.Name, cancellationToken) : "-";

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
