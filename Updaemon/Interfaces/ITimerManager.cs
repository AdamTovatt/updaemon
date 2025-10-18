namespace Updaemon.Interfaces
{
    /// <summary>
    /// Manages systemd timers for automatic updates.
    /// </summary>
    public interface ITimerManager
    {
        /// <summary>
        /// Sets up a systemd timer to run updaemon update at the specified interval.
        /// </summary>
        Task SetTimerAsync(TimeSpan interval, CancellationToken cancellationToken = default);

        /// <summary>
        /// Disables the automatic update timer.
        /// </summary>
        Task DisableTimerAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the automatic update timer is currently enabled.
        /// </summary>
        Task<bool> IsTimerEnabledAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current interval of the timer if enabled.
        /// </summary>
        Task<string?> GetCurrentIntervalAsync(CancellationToken cancellationToken = default);
    }
}