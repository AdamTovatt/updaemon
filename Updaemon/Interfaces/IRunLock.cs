namespace Updaemon.Interfaces
{
    /// <summary>
    /// Serializes state-changing updaemon runs within one scope.
    /// </summary>
    public interface IRunLock
    {
        /// <summary>
        /// Checks that the scope's directories are writable, then takes the scope's exclusive lock,
        /// waiting while another run holds it. Dispose the result to release the lock.
        /// </summary>
        /// <exception cref="InvalidOperationException">A scope directory cannot be created or written.</exception>
        Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default);
    }
}
