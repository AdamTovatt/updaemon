using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Linux implementation of <see cref="ITimerManager"/> backed by systemd timer + service units.
    /// Constructed only on Linux by Program.cs.
    /// </summary>
    public class LinuxTimerManager : ITimerManager
    {
        private readonly string _timerUnitPath;
        private readonly string _serviceUnitPath;

        public LinuxTimerManager()
            : this("/etc/systemd/system/updaemon.timer", "/etc/systemd/system/updaemon.service")
        {
        }

        public LinuxTimerManager(string timerUnitPath, string serviceUnitPath)
        {
            _timerUnitPath = timerUnitPath;
            _serviceUnitPath = serviceUnitPath;
        }

        public async Task SetTimerAsync(TimeSpan interval, CancellationToken cancellationToken = default)
        {
            await CreateServiceUnitFileAsync(cancellationToken);
            await CreateTimerUnitFileAsync(interval, cancellationToken);
            await ProcessRunner.RunAndCheckAsync("systemctl", new[] { "daemon-reload" }, cancellationToken);
            await ProcessRunner.RunAndCheckAsync("systemctl", new[] { "enable", "updaemon.timer" }, cancellationToken);
            await ProcessRunner.RunAndCheckAsync("systemctl", new[] { "start", "updaemon.timer" }, cancellationToken);
        }

        public async Task DisableTimerAsync(CancellationToken cancellationToken = default)
        {
            // Both stop and disable are best-effort: the timer may not exist or be running.
            await ProcessRunner.RunAsync("systemctl", new[] { "stop", "updaemon.timer" }, cancellationToken);
            await ProcessRunner.RunAsync("systemctl", new[] { "disable", "updaemon.timer" }, cancellationToken);
        }

        public async Task<bool> IsTimerEnabledAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                ProcessRunner.Result result = await ProcessRunner.RunAsync("systemctl", new[] { "is-enabled", "updaemon.timer" }, cancellationToken);
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

        private async Task CreateServiceUnitFileAsync(CancellationToken cancellationToken)
        {
            string serviceContent = @"[Unit]
Description=Updaemon update service

[Service]
Type=oneshot
ExecStart=/usr/local/bin/updaemon update
";

            await File.WriteAllTextAsync(_serviceUnitPath, serviceContent, cancellationToken);
        }

        private async Task CreateTimerUnitFileAsync(TimeSpan interval, CancellationToken cancellationToken)
        {
            string onCalendar = ConvertToSystemdCalendar(interval);

            string timerContent = $@"[Unit]
Description=Run updaemon update periodically

[Timer]
OnCalendar={onCalendar}
Persistent=true

[Install]
WantedBy=timers.target
";

            await File.WriteAllTextAsync(_timerUnitPath, timerContent, cancellationToken);
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
