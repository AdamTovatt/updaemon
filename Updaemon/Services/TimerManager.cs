using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Manages systemd timers for automatic updates.
    /// </summary>
    public class TimerManager : ITimerManager
    {
        private readonly IOutputWriter _outputWriter;
        private readonly string _timerUnitPath;
        private readonly string _serviceUnitPath;

        public TimerManager(IOutputWriter outputWriter)
        {
            _outputWriter = outputWriter;
            _timerUnitPath = "/etc/systemd/system/updaemon.timer";
            _serviceUnitPath = "/etc/systemd/system/updaemon.service";
        }

        public TimerManager(IOutputWriter outputWriter, string timerUnitPath, string serviceUnitPath)
        {
            _outputWriter = outputWriter;
            _timerUnitPath = timerUnitPath;
            _serviceUnitPath = serviceUnitPath;
        }

        public async Task SetTimerAsync(TimeSpan interval, CancellationToken cancellationToken = default)
        {
            // Create the service unit file
            await CreateServiceUnitFileAsync(cancellationToken);

            // Create the timer unit file
            await CreateTimerUnitFileAsync(interval, cancellationToken);

            // Reload systemd and enable the timer
            await ReloadSystemdAsync(cancellationToken);
            await EnableTimerAsync(cancellationToken);
        }

        public async Task DisableTimerAsync(CancellationToken cancellationToken = default)
        {
            // Stop and disable the timer
            await StopTimerAsync(cancellationToken);
            await DisableTimerUnitAsync(cancellationToken);
        }

        public async Task<bool> IsTimerEnabledAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                string result = await RunCommandAsync("systemctl", "is-enabled updaemon.timer", cancellationToken);
                return result.Trim() == "enabled";
            }
            catch
            {
                return false;
            }
        }

        public async Task<string?> GetCurrentIntervalAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(_timerUnitPath))
                    return null;

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
            catch
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
            // Convert TimeSpan to systemd OnCalendar format
            if (interval.TotalMinutes < 1)
            {
                // For intervals less than 1 minute, use seconds
                int seconds = (int)interval.TotalSeconds;
                return $"*:*:0/{seconds}";
            }
            else if (interval.TotalHours < 1)
            {
                // For intervals less than 1 hour, use minutes
                int minutes = (int)interval.TotalMinutes;
                return $"*:0/{minutes}:00";
            }
            else
            {
                // For intervals of 1 hour or more, use hours
                int hours = (int)interval.TotalHours;
                return $"0/{hours}:00:00";
            }
        }

        private async Task ReloadSystemdAsync(CancellationToken cancellationToken)
        {
            await RunCommandAsync("systemctl", "daemon-reload", cancellationToken);
        }

        private async Task EnableTimerAsync(CancellationToken cancellationToken)
        {
            await RunCommandAsync("systemctl", "enable updaemon.timer", cancellationToken);
            await RunCommandAsync("systemctl", "start updaemon.timer", cancellationToken);
        }

        private async Task StopTimerAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RunCommandAsync("systemctl", "stop updaemon.timer", cancellationToken);
            }
            catch
            {
                // Timer might not be running, ignore errors
            }
        }

        private async Task DisableTimerUnitAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RunCommandAsync("systemctl", "disable updaemon.timer", cancellationToken);
            }
            catch
            {
                // Timer might not be enabled, ignore errors
            }
        }

        private async Task<string> RunCommandAsync(string command, string arguments, CancellationToken cancellationToken)
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = command;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException($"Command failed: {command} {arguments}\nError: {error}");
            }

            return output;
        }
    }
}