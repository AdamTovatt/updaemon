using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Linux-specific version extractor that parses version numbers from file paths.
    /// </summary>
    public class VersionExtractor : IVersionExtractor
    {
        public Version? ExtractVersionFromPath(string? path)
        {
            if (path == null)
                return null;

            string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
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

