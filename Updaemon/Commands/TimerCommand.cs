using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'timer' command to manage automatic update scheduling.
    /// </summary>
    public class TimerCommand : ICommand
    {
        private readonly ITimerManager _timerManager;
        private readonly IServiceManager _serviceManager;
        private readonly IConfigManager _configManager;
        private readonly IOutputWriter _outputWriter;

        public TimerCommand(ITimerManager timerManager, IServiceManager serviceManager, IConfigManager configManager, IOutputWriter outputWriter)
        {
            _timerManager = timerManager;
            _serviceManager = serviceManager;
            _configManager = configManager;
            _outputWriter = outputWriter;
        }

        public string Name => "timer";

        public string Description => "Manage automatic update timer";

        public string Usage => "updaemon timer [<interval> [--keep] | -]";

        /// <summary>Sets the interval only when no timer was ever set; install.sh turns the timer on with it.</summary>
        private const string KeepOption = "--keep";

        public bool ChangesState => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, KeepOption);
            if (parser.GetUnknownFlags(KeepOption) is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            bool keep = parser.HasFlag(KeepOption);
            string? interval = parser.GetPositional(0);
            if (parser.Positionals.Count > 1 || (keep && (interval == null || interval == "-")))
            {
                return _outputWriter.FailUsage(this);
            }

            // The timer is a unit or plist of the same service manager; without it, nothing is written.
            ReachableServiceManager reachable = await _serviceManager.EnsureReachableAsync(cancellationToken);

            if (keep && await DescribeKeptTimerAsync(cancellationToken) is string kept)
            {
                _outputWriter.WriteLine(kept);
                return 0;
            }

            if (string.IsNullOrEmpty(interval))
            {
                // Show current timer status
                await ShowCurrentStatusAsync(cancellationToken);
                return 0;
            }

            if (interval == "-")
            {
                await RemoveTimerAsync(reachable, cancellationToken);
                return 0;
            }

            // Set timer with specified interval
            int result = await SetTimerAsync(interval, reachable, cancellationToken);
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

        /// <summary>
        /// What --keep leaves as it is, said in one line; null when no timer was ever set. A timer
        /// turned off (the config's record, which the 0.x conversion also writes) stays off, and one
        /// that is set keeps its interval.
        /// </summary>
        private async Task<string?> DescribeKeptTimerAsync(CancellationToken cancellationToken)
        {
            if ((await _configManager.LoadConfigAsync(cancellationToken)).TimerTurnedOff)
            {
                return "The update timer stays off, as it was turned off. Turn it on with 'updaemon timer 1h'.";
            }

            return await _timerManager.GetCurrentIntervalAsync(cancellationToken) is string existing
                ? $"The update timer stays as it was ({existing}). Turn it off with 'updaemon timer -'."
                : null;
        }

        private async Task RemoveTimerAsync(ReachableServiceManager reachable, CancellationToken cancellationToken)
        {
            // Recorded first: if removing then fails halfway, the timer is still meant to be off, and install.sh leaves it so.
            await RecordTurnedOffAsync(true, cancellationToken);
            await _timerManager.RemoveTimerAsync(reachable, cancellationToken);
            _outputWriter.WriteLine("The update timer is off. Turn it on with 'updaemon timer 1h'.");
        }

        private async Task RecordTurnedOffAsync(bool turnedOff, CancellationToken cancellationToken)
        {
            UpdaemonConfig config = await _configManager.LoadConfigAsync(cancellationToken);
            if (config.TimerTurnedOff != turnedOff)
            {
                config.TimerTurnedOff = turnedOff;
                await _configManager.SaveConfigAsync(config, cancellationToken);
            }
        }

        private async Task<int> SetTimerAsync(string interval, ReachableServiceManager reachable, CancellationToken cancellationToken)
        {
            // Parse the interval
            TimeSpan? parsedInterval = ParseInterval(interval);
            if (parsedInterval == null)
            {
                _outputWriter.WriteError($"Error: Invalid interval format '{interval}'");
                _outputWriter.WriteLine("Supported formats: 30s, 5m, 1h");
                return 1;
            }

            // Cleared first: a record left saying off while a timer runs would make --keep call it off.
            await RecordTurnedOffAsync(false, cancellationToken);
            await _timerManager.SetTimerAsync(parsedInterval.Value, reachable, cancellationToken);
            _outputWriter.WriteLine($"The update timer runs 'updaemon update' every {interval}. Turn it off with 'updaemon timer -'.");
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
                  updaemon timer [<interval> [--keep]]
                  updaemon timer -

                Description:
                  Manages the automatic update timer. Without arguments, shows the current
                  timer status. With an interval, sets the timer to run updates at that
                  interval. Use "-" to turn the timer off, which deletes its unit or plist.
                  With --keep, a timer that is set keeps its interval and one turned off
                  stays off; install.sh uses this.

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