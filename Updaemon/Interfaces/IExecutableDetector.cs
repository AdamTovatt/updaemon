namespace Updaemon.Interfaces
{
    /// <summary>
    /// Finds an executable by name in a release that ships no manifest.
    /// </summary>
    public interface IExecutableDetector
    {
        /// <summary>
        /// Finds the file named <paramref name="executableName"/> (ignoring case) anywhere under
        /// <paramref name="directoryPath"/>, else the first file whose name contains it, else null.
        /// </summary>
        Task<string?> FindExecutableAsync(string directoryPath, string executableName, CancellationToken cancellationToken = default);
    }
}
