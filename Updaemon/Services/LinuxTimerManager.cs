using System.Text.RegularExpressions;
using Updaemon.Configuration;
using Updaemon.Conversion;
using Updaemon.Interfaces;
using Updaemon.IO;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Linux implementation of <see cref="ITimerManager"/> backed by a systemd timer and service
    /// unit in the scope's unit directory (system or user manager).
    /// </summary>
    public partial class LinuxTimerManager : ITimerManager
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

        public async Task SetTimerAsync(TimeSpan interval, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            await AtomicFile.WriteAsync(_serviceUnitPath, BuildServiceUnit(_scope.UpdaemonCommandPath), AtomicFile.Readable, cancellationToken);
            await AtomicFile.WriteAsync(_timerUnitPath, BuildTimerUnit(interval), AtomicFile.Readable, cancellationToken);
            await ProcessRunner.RunAndCheckAsync("systemctl", Arguments("daemon-reload"), cancellationToken);
            await ProcessRunner.RunAndCheckAsync("systemctl", Arguments("enable", _timerUnitName), cancellationToken);
            await ProcessRunner.RunAndCheckAsync("systemctl", Arguments("start", _timerUnitName), cancellationToken);
        }

        public async Task RemoveTimerAsync(ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            // Both stop and disable are best-effort: the timer may not exist or be running.
            await ProcessRunner.RunAsync("systemctl", Arguments("stop", _timerUnitName), cancellationToken);
            await ProcessRunner.RunAsync("systemctl", Arguments("disable", _timerUnitName), cancellationToken);
            File.Delete(_timerUnitPath);
            File.Delete(_serviceUnitPath);
            await ProcessRunner.RunAndCheckAsync("systemctl", Arguments("daemon-reload"), cancellationToken);
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

        public async Task<TimeSpan?> GetCurrentIntervalAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return File.Exists(_timerUnitPath) ? ReadInterval(await File.ReadAllTextAsync(_timerUnitPath, cancellationToken)) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public async Task<ZeroXTimer?> FindZeroXTimerAsync(CancellationToken cancellationToken = default)
        {
            if (!_scope.IsSystem || !File.Exists(_timerUnitPath))
            {
                return null;
            }

            bool enabled = await IsTimerEnabledAsync(cancellationToken);
            string? serviceUnit = File.Exists(_serviceUnitPath) ? await File.ReadAllTextAsync(_serviceUnitPath, cancellationToken) : null;
            return ZeroXTimerFrom(enabled, await File.ReadAllTextAsync(_timerUnitPath, cancellationToken), serviceUnit);
        }

        /// <summary>
        /// 0.x's timer units have the system scope's names. One turned off was left behind, disabled, by
        /// 0.x's 'timer -', where this updaemon deletes them: it is found as off, to be removed. One that
        /// is on runs 'update' without <see cref="TimerRun.Option"/>, so its runs
        /// would read as started by hand: it is found as on, to be rewritten in place with its interval.
        /// One whose interval cannot be read is left as it is rather than turned off.
        /// </summary>
        internal ZeroXTimer? ZeroXTimerFrom(bool enabled, string timerUnit, string? serviceUnit)
        {
            string[] files = serviceUnit == null ? [_timerUnitPath] : [_timerUnitPath, _serviceUnitPath];
            if (!enabled)
            {
                return new ZeroXTimer(files, IsOn: false, Interval: null);
            }

            if (serviceUnit == BuildServiceUnit(_scope.UpdaemonCommandPath) || ReadInterval(timerUnit) is not TimeSpan interval)
            {
                return null;
            }

            return new ZeroXTimer(files, IsOn: true, interval);
        }

        /// <summary>
        /// A timer that is on was rewritten in place by <see cref="SetTimerAsync"/>, under the same names,
        /// so there is nothing left to remove.
        /// </summary>
        public Task RemoveZeroXTimerAsync(ZeroXTimer timer, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            return timer.IsOn ? Task.CompletedTask : RemoveTimerAsync(reachable, cancellationToken);
        }

        /// <summary>The interval of a timer unit <see cref="BuildTimerUnit"/> wrote, or null.</summary>
        internal static TimeSpan? ReadInterval(string timerUnit)
        {
            Match match = CalendarLine().Match(timerUnit);
            if (!match.Success)
            {
                return null;
            }

            Group seconds = match.Groups["s"], minutes = match.Groups["m"], hours = match.Groups["h"];
            return seconds.Success ? TimeSpan.FromSeconds(int.Parse(seconds.Value))
                : minutes.Success ? TimeSpan.FromMinutes(int.Parse(minutes.Value))
                : TimeSpan.FromHours(int.Parse(hours.Value));
        }

        [GeneratedRegex(@"^OnCalendar=(?:\*:\*:0/(?<s>\d{1,9})|\*:0/(?<m>\d{1,9}):00|0/(?<h>\d{1,9}):00:00)$", RegexOptions.Multiline)]
        private static partial Regex CalendarLine();

        private string[] Arguments(params string[] verb) => [.. _scope.SystemdScopeArguments, .. verb];

        internal static string BuildServiceUnit(string updaemonPath)
        {
            return $"""
                [Unit]
                Description=Updaemon update service

                [Service]
                Type=oneshot
                ExecStart={SystemdSyntax.QuoteArgument(updaemonPath)} {string.Join(' ', TimerRun.Arguments)}

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
