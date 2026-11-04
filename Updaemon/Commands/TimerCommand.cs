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

        /// <summary>The lines setting and turning off the timer print, so showing it reads the same.</summary>
        private const string IsOff = "The update timer is off. Turn it on with 'updaemon timer 1h'.";

        private const string IntervalUnreadable = "The update timer is on, but its interval cannot be read. Set it with 'updaemon timer <interval>'.";

        private static string RunsEvery(TimeSpan interval) =>
            $"The update timer runs 'updaemon update' every {TimerInterval.Format(interval)}. Turn it off with 'updaemon timer -'.";

        /// <summary>Plain 'timer' only shows it.</summary>
        public bool ChangesState(string[] args) => args.Length > 0;

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
            _outputWriter.WriteLine(await TimerState.ReadAsync(_timerManager, cancellationToken) switch
            {
                TimerState.On { Interval: TimeSpan interval } => RunsEvery(interval),
                TimerState.On => IntervalUnreadable,
                _ => IsOff,
            });
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

            return await _timerManager.GetCurrentIntervalAsync(cancellationToken) is TimeSpan existing
                ? $"The update timer stays as it was ({TimerInterval.Format(existing)}). Turn it off with 'updaemon timer -'."
                : null;
        }

        private async Task RemoveTimerAsync(ReachableServiceManager reachable, CancellationToken cancellationToken)
        {
            // Recorded first: if removing then fails halfway, the timer is still meant to be off, and install.sh leaves it so.
            await RecordTurnedOffAsync(true, cancellationToken);
            await _timerManager.RemoveTimerAsync(reachable, cancellationToken);
            _outputWriter.WriteLine(IsOff);
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
            TimeSpan? parsedInterval = TimerInterval.Parse(interval);
            if (parsedInterval == null)
            {
                _outputWriter.WriteError($"Error: Invalid interval format '{interval}'");
                _outputWriter.WriteLine("Supported formats: 30s, 5m, 1h");
                return 1;
            }

            // Cleared first: a record left saying off while a timer runs would make --keep call it off.
            await RecordTurnedOffAsync(false, cancellationToken);
            await _timerManager.SetTimerAsync(parsedInterval.Value, reachable, cancellationToken);
            _outputWriter.WriteLine(RunsEvery(parsedInterval.Value));
            return 0;
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