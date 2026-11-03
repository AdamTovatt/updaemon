using Updaemon.Configuration;
using Updaemon.Conversion;
using Updaemon.Interfaces;
using Updaemon.IO;

namespace Updaemon.Services
{
    /// <summary>
    /// Linux implementation of <see cref="ITimerManager"/> backed by a systemd timer and service
    /// unit in the scope's unit directory (system or user manager).
    /// </summary>
    public class LinuxTimerManager : ITimerManager
    {
        private readonly UpdaemonScope _scope;
        private readonly string _timerUnitName;
        private readonly string _timerUnitPath;
        private readonly string _serviceUnitPath;

        public LinuxTimerManager(UpdaemonScope scope)
        {
            _scope = scope;
            _timerUnitName = scope.TimerLabel + ".timer";
            _timerUnitPath = Path.Combine(scope.UnitDirectory, _timerUnitName);
            _serviceUnitPath = Path.Combine(scope.UnitDirectory, scope.TimerLabel + ".service");
        }

        public async Task SetTimerAsync(TimeSpan interval, CancellationToken cancellationToken = default)
        {
            await AtomicFile.WriteAsync(_serviceUnitPath, BuildServiceUnit(_scope.UpdaemonCommandPath), AtomicFile.Readable, cancellationToken);
            await AtomicFile.WriteAsync(_timerUnitPath, BuildTimerUnit(interval), AtomicFile.Readable, cancellationToken);
            await ProcessRunner.RunAndCheckAsync("systemctl", Arguments("daemon-reload"), cancellationToken);
            await ProcessRunner.RunAndCheckAsync("systemctl", Arguments("enable", _timerUnitName), cancellationToken);
            await ProcessRunner.RunAndCheckAsync("systemctl", Arguments("start", _timerUnitName), cancellationToken);
        }

        public async Task DisableTimerAsync(CancellationToken cancellationToken = default)
        {
            // Both stop and disable are best-effort: the timer may not exist or be running.
            await ProcessRunner.RunAsync("systemctl", Arguments("stop", _timerUnitName), cancellationToken);
            await ProcessRunner.RunAsync("systemctl", Arguments("disable", _timerUnitName), cancellationToken);
        }

        public async Task<bool> IsTimerEnabledAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                ProcessRunner.Result result = await ProcessRunner.RunAsync("systemctl", Arguments("is-enabled", _timerUnitName), cancellationToken);
                return result.StdOut.Trim() == "enabled";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return false;
            }
        }

        public async Task<string?> GetCurrentIntervalAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(_timerUnitPath))
                {
                    return null;
                }

                string[] lines = await File.ReadAllLinesAsync(_timerUnitPath, cancellationToken);

                foreach (string line in lines)
                {
                    if (line.StartsWith("OnCalendar=", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring("OnCalendar=".Length).Trim();
                    }
                }

                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// Always null: 0.x's timer units have the system scope's names and run the same command, so
        /// they already are this updaemon's timer.
        /// </summary>
        public Task<ZeroXTimer?> FindZeroXTimerAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ZeroXTimer?>(null);
        }

        public Task RemoveZeroXTimerAsync(ZeroXTimer timer, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("On Linux, updaemon 0.x's timer is this updaemon's own; there is none to remove.");
        }

        private string[] Arguments(params string[] verb) => [.. _scope.SystemdScopeArguments, .. verb];

        internal static string BuildServiceUnit(string updaemonPath)
        {
            return $"""
                [Unit]
                Description=Updaemon update service

                [Service]
                Type=oneshot
                ExecStart={SystemdSyntax.QuoteArgument(updaemonPath)} update

                """;
        }

        internal static string BuildTimerUnit(TimeSpan interval)
        {
            return $"""
                [Unit]
                Description=Run updaemon update periodically

                [Timer]
                OnCalendar={ConvertToSystemdCalendar(interval)}
                Persistent=true

                [Install]
                WantedBy=timers.target

                """;
        }

        private static string ConvertToSystemdCalendar(TimeSpan interval)
        {
            // Clamp any nonsense at the boundary; systemd OnCalendar wants positive integers.
            // The supported time formats (30s, 5m, 1h) cap well below int.MaxValue in practice.
            int Clamp(double value)
            {
                if (double.IsNaN(value) || value < 1) return 1;
                if (value > int.MaxValue) return int.MaxValue;
                return (int)value;
            }

            if (interval.TotalMinutes < 1)
            {
                return $"*:*:0/{Clamp(interval.TotalSeconds)}";
            }
            if (interval.TotalHours < 1)
            {
                return $"*:0/{Clamp(interval.TotalMinutes)}:00";
            }
            return $"0/{Clamp(interval.TotalHours)}:00:00";
        }
    }
}
