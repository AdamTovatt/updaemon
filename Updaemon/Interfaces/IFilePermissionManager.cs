namespace Updaemon.Interfaces
{
    /// <summary>
    /// Sets modes and ownership on deployed files.
    /// </summary>
    public interface IFilePermissionManager
    {
        /// <summary>Makes a file executable by everyone (0755).</summary>
        Task SetExecutablePermissionsAsync(string executablePath, CancellationToken cancellationToken = default);

        /// <summary>Makes a directory tree readable by everyone (<c>chmod -R a+rX</c>).</summary>
        Task SetDirectoryPermissionsAsync(string directoryPath, CancellationToken cancellationToken = default);

        /// <summary>Gives <paramref name="path"/> to <paramref name="user"/>.</summary>
        Task SetOwnerAsync(string path, string user, CancellationToken cancellationToken = default);

        /// <summary>Whether a user account with this name exists on the machine.</summary>
        Task<bool> UserExistsAsync(string user, CancellationToken cancellationToken = default);
    }
}
