namespace Updaemon.Interfaces
{
    /// <summary>
    /// Detects executable files within service directories.
    /// </summary>
    public interface IExecutableDetector
    {
        /// <summary>
        /// Finds the executable file for a service in the specified directory.
        /// </summary>
        /// <param name="directoryPath">The directory to search in.</param>
        /// <param name="serviceName">The service name to match against.</param>
        /// <returns>The path to the executable, or null if not found.</returns>
        Task<string?> FindExecutableAsync(string directoryPath, string serviceName);
    }
}

