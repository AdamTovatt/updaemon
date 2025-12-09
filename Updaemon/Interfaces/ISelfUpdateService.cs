namespace Updaemon.Interfaces
{
    /// <summary>
    /// Service for checking and updating updaemon itself.
    /// </summary>
    public interface ISelfUpdateService
    {
        /// <summary>
        /// Checks for a newer version of updaemon and updates if available.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>True if an update was triggered (process should exit), false otherwise.</returns>
        Task<bool> CheckAndUpdateAsync(CancellationToken cancellationToken = default);
    }
}

