namespace Updaemon.Interfaces
{
    /// <summary>
    /// Extracts version information from file paths.
    /// </summary>
    public interface IVersionExtractor
    {
        /// <summary>
        /// Extracts a version number from a file path by parsing path segments.
        /// </summary>
        /// <param name="path">The file path to parse (can be null).</param>
        /// <returns>The first valid version found in the path segments, or null if no version found.</returns>
        Version? ExtractVersionFromPath(string? path);
    }
}

