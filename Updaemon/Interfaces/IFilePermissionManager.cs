namespace Updaemon.Interfaces
{
    /// <summary>
    /// Manages file permissions for executables and directories.
    /// </summary>
    public interface IFilePermissionManager
    {
        /// <summary>
        /// Makes a file executable by setting the appropriate permissions.
        /// </summary>
        /// <param name="executablePath">The path to the file to make executable.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        Task SetExecutablePermissionsAsync(string executablePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets read and execute permissions recursively on a directory.
        /// </summary>
        /// <param name="directoryPath">The path to the directory.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        Task SetDirectoryPermissionsAsync(string directoryPath, CancellationToken cancellationToken = default);
    }
}

