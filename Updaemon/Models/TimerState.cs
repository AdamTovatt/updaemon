using Updaemon.Interfaces;

namespace Updaemon.Models
{
    /// <summary>The update timer as it is: off, or on with the interval its file names when that can be read.</summary>
    public abstract record TimerState
    {
        private TimerState()
        {
        }

        public sealed record Off : TimerState;

        /// <param name="Interval">Null when the timer's file names none updaemon can read.</param>
        public sealed record On(TimeSpan? Interval) : TimerState;

        public static async Task<TimerState> ReadAsync(ITimerManager timers, CancellationToken cancellationToken)
        {
            return await timers.IsTimerEnabledAsync(cancellationToken)
                ? new On(await timers.GetCurrentIntervalAsync(cancellationToken))
                : new Off();
        }
    }
}
