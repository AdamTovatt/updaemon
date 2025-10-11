namespace Updaemon.Interfaces
{
    /// <summary>
    /// Manages symbolic links for service executables.
    /// </summary>
    public interface ISymlinkManager
    {
        /// <summary>
        /// Creates or updates a symbolic link.
        /// </summary>
        /// <param name="linkPath">The path where the symlink should be created.</param>
        /// <param name="targetPath">The path the symlink should point to.</param>
        Task CreateOrUpdateSymlinkAsync(string linkPath, string targetPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the target path of a symbolic link.
        /// </summary>
        /// <param name="linkPath">The path of the symlink to read.</param>
        /// <returns>The target path, or null if the symlink doesn't exist.</returns>
        Task<string?> ReadSymlinkAsync(string linkPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a path is a symbolic link.
        /// </summary>
        Task<bool> IsSymlinkAsync(string path, CancellationToken cancellationToken = default);
    }
}

