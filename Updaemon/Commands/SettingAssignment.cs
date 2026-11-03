using System.Diagnostics.CodeAnalysis;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Parses a <c>KEY=VALUE</c> argument given to <c>install --set</c> or <c>set</c>.
    /// </summary>
    public static class SettingAssignment
    {
        public static bool TryParse(string assignment, out string key, out string value, [NotNullWhen(false)] out string? error)
        {
            int separator = assignment.IndexOf('=');
            key = separator > 0 ? assignment.Substring(0, separator) : assignment;
            value = separator > 0 ? assignment.Substring(separator + 1) : string.Empty;

            if (separator <= 0)
            {
                error = $"'{assignment}' is not KEY=VALUE.";
                return false;
            }

            if (!AppManifest.IsValidSettingKey(key))
            {
                error = $"'{key}' is not a valid environment variable name.";
                return false;
            }

            if (key == AppManifest.DataDirectoryVariable)
            {
                error = $"{AppManifest.DataDirectoryVariable} is set by updaemon.";
                return false;
            }

            if (!SafeText.IsSafe(value))
            {
                error = $"The value of {key} contains a control character, such as a line break, which updaemon cannot store.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
