using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'timer' command to manage automatic update scheduling.
    /// </summary>
    public class TimerCommand
    {
        private readonly ITimerManager _timerManager;
        private readonly IOutputWriter _outputWriter;

        public TimerCommand(ITimerManager timerManager, IOutputWriter outputWriter)
        {
            _timerManager = timerManager;
            _outputWriter = outputWriter;
        }

        public async Task ExecuteAsync(string? interval, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(interval))
            {
                // Show current timer status
                await ShowCurrentStatusAsync(cancellationToken);
                return;
            }

            if (interval == "-")
            {
                // Disable timer
                await DisableTimerAsync(cancellationToken);
                return;
            }

            // Set timer with specified interval
            await SetTimerAsync(interval, cancellationToken);
        }

        private async Task ShowCurrentStatusAsync(CancellationToken cancellationToken)
        {
            bool isEnabled = await _timerManager.IsTimerEnabledAsync(cancellationToken);
            
            if (isEnabled)
            {
                string? interval = await _timerManager.GetCurrentIntervalAsync(cancellationToken);
                _outputWriter.WriteLine($"Timer is enabled with interval: {interval ?? "unknown"}");
            }
            else
            {
                _outputWriter.WriteLine("Timer is disabled");
            }
        }

        private async Task DisableTimerAsync(CancellationToken cancellationToken)
        {
            _outputWriter.WriteLine("Disabling automatic update timer...");
            await _timerManager.DisableTimerAsync(cancellationToken);
            _outputWriter.WriteLine("Timer disabled successfully");
        }

        private async Task SetTimerAsync(string interval, CancellationToken cancellationToken)
        {
            // Parse the interval
            TimeSpan? parsedInterval = ParseInterval(interval);
            if (parsedInterval == null)
            {
                _outputWriter.WriteError($"Error: Invalid interval format '{interval}'");
                _outputWriter.WriteLine("Supported formats: 30s, 5m, 1h");
                return;
            }

            _outputWriter.WriteLine($"Setting automatic update timer to run every {interval}...");
            await _timerManager.SetTimerAsync(parsedInterval.Value, cancellationToken);
            _outputWriter.WriteLine($"Timer set successfully to run every {interval}");
        }

        private static TimeSpan? ParseInterval(string interval)
        {
            if (string.IsNullOrEmpty(interval))
                return null;

            interval = interval.Trim().ToLowerInvariant();

            // Parse format like "30s", "5m", "1h"
            if (interval.Length < 2)
                return null;

            string numberPart = interval[..^1];
            char unit = interval[^1];

            if (!int.TryParse(numberPart, out int value) || value <= 0)
                return null;

            return unit switch
            {
                's' => TimeSpan.FromSeconds(value),
                'm' => TimeSpan.FromMinutes(value),
                'h' => TimeSpan.FromHours(value),
                _ => null
            };
        }
    }
}