using Updaemon.Interfaces;

namespace Updaemon.Tests.Helpers
{
    /// <summary>
    /// Platform-agnostic version extractor for testing purposes.
    /// Supports both Windows and Linux path separators.
    /// </summary>
    public class TestVersionExtractor : IVersionExtractor
    {
        public Version? ExtractVersionFromPath(string? path)
        {
            if (path == null)
                return null;

            char[] separators = new char[] { '/', '\\' };
            string[] parts = path.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                if (Version.TryParse(part, out Version? version))
                {
                    return version;
                }
            }

            return null;
        }
    }
}

