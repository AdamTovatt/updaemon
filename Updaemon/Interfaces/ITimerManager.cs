using Updaemon.Conversion;

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
        /// Stops the timer and deletes its files, so no boot or login brings it back. Quiet when there is none.
        /// </summary>
        Task RemoveTimerAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the automatic update timer is currently enabled.
        /// </summary>
        Task<bool> IsTimerEnabledAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a human-readable representation of the interval in the timer's file, or null when there is none.
        /// </summary>
        Task<string?> GetCurrentIntervalAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Updaemon 0.x's update timer where it is not this updaemon's own, or null. Changes nothing.
        /// </summary>
        Task<ZeroXTimer?> FindZeroXTimerAsync(CancellationToken cancellationToken = default);

        /// <summary>Stops a timer from <see cref="FindZeroXTimerAsync"/> and deletes its file.</summary>
        Task RemoveZeroXTimerAsync(ZeroXTimer timer, CancellationToken cancellationToken = default);
    }
}