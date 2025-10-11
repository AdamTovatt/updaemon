namespace Updaemon.GithubDistributionService.Interfaces
{
    /// <summary>
    /// Parses version strings from GitHub release tag names.
    /// </summary>
    public interface IVersionParser
    {
        /// <summary>
        /// Parses a GitHub tag name into a Version object.
        /// </summary>
        /// <param name="tagName">The tag name from a GitHub release (e.g., "v1.2.3", "curl-8_16_0")</param>
        /// <returns>The parsed Version, or null if parsing fails</returns>
        Version? Parse(string tagName);
    }
}

