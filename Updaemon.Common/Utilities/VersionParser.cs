using System.Text;
using System.Text.RegularExpressions;

namespace Updaemon.Common.Utilities
{
    /// <summary>
    /// Parses version strings from GitHub release tag names by extracting numeric components.
    /// </summary>
    public class VersionParser : IVersionParser
    {
        public Version? Parse(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            // Extract all sequences of digits
            MatchCollection matches = Regex.Matches(tagName, @"\d+");
            if (matches.Count == 0)
            {
                return null;
            }

            // Build version string by joining numeric parts with dots
            StringBuilder versionBuilder = new StringBuilder();
            foreach (Match match in matches)
            {
                if (versionBuilder.Length > 0)
                {
                    versionBuilder.Append('.');
                }

                versionBuilder.Append(match.Value);
            }

            string versionString = versionBuilder.ToString();

            // Try to parse as System.Version
            if (Version.TryParse(versionString, out Version? version))
            {
                return version;
            }

            return null;
        }
    }
}

