namespace Updaemon.Interfaces
{
    /// <summary>
    /// Converts an updaemon 0.x setup into this updaemon's.
    /// </summary>
    public interface IZeroXConverter
    {
        /// <summary>Whether this scope's config is still 0.x's: only the system scope's can be.</summary>
        bool IsPending();

        /// <summary>
        /// Converts the 0.x setup: all of it the first time, afterwards what is not converted yet.
        /// With <paramref name="dryRun"/>, only says what it would do. Returns whether everything was
        /// converted; what was not is reported with the reason.
        /// </summary>
        Task<bool> RunAsync(bool dryRun, CancellationToken cancellationToken = default);
    }
}
