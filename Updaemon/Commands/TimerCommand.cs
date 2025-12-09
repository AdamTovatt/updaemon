using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'timer' command to manage automatic update scheduling.
    /// </summary>
    public class TimerCommand : ICommand
    {
        private readonly ITimerManager _timerManager;
        private readonly IOutputWriter _outputWriter;

        public TimerCommand(ITimerManager timerManager, IOutputWriter outputWriter)
        {
            _timerManager = timerManager;
            _outputWriter = outputWriter;
        }

        public string Name => "timer";

        public string Description => "Manage automatic update timer";

        public string Usage => "updaemon timer [interval]";

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            string? interval = args.Length > 0 ? args[0] : null;

            if (string.IsNullOrEmpty(interval))
            {
                // Show current timer status
                await ShowCurrentStatusAsync(cancellationToken);
                return 0;
            }

            if (interval == "-")
            {
                // Disable timer
                await DisableTimerAsync(cancellationToken);
                return 0;
            }

            // Set timer with specified interval
            int result = await SetTimerAsync(interval, cancellationToken);
            return result;
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

        private async Task<int> SetTimerAsync(string interval, CancellationToken cancellationToken)
        {
            // Parse the interval
            TimeSpan? parsedInterval = ParseInterval(interval);
            if (parsedInterval == null)
            {
                _outputWriter.WriteError($"Error: Invalid interval format '{interval}'");
                _outputWriter.WriteLine("Supported formats: 30s, 5m, 1h");
                return 1;
            }

            _outputWriter.WriteLine($"Setting automatic update timer to run every {interval}...");
            await _timerManager.SetTimerAsync(parsedInterval.Value, cancellationToken);
            _outputWriter.WriteLine($"Timer set successfully to run every {interval}");
            return 0;
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

        public string GetDetailedHelp()
        {
            return """
                Timer Command

                Usage:
                  updaemon timer [interval]
                  updaemon timer -

                Description:
                  Manages the automatic update timer. Without arguments, shows the current
                  timer status. With an interval, sets the timer to run updates at that
                  interval. Use "-" to disable the timer.

                Interval Formats:
                  - 30s  (30 seconds)
                  - 5m   (5 minutes)
                  - 1h   (1 hour)

                Examples:
                  updaemon timer          # Show current status
                  updaemon timer 10m      # Set timer to 10 minutes
                  updaemon timer 1h       # Set timer to 1 hour
                  updaemon timer -        # Disable timer
                """;
        }
    }
}