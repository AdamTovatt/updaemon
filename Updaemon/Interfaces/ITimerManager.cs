namespace Updaemon.Interfaces
{
    /// <summary>
    /// Manages the recurring "updaemon update" timer. Implemented via a systemd timer
    /// on Linux and a launchd plist with StartInterval on macOS.
    /// </summary>
    public interface ITimerManager
    {
        /// <summary>
        /// Sets up a recurring timer to run "updaemon update" at the specified interval.
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
        /// Gets a human-readable representation of the current interval if the timer is enabled.
        /// </summary>
        Task<string?> GetCurrentIntervalAsync(CancellationToken cancellationToken = default);
    }
}