using System.Text;
using Updaemon.IO;
using Updaemon.Models;

namespace Updaemon.Configuration
{
    /// <summary>
    /// The <c>KEY="value"</c> environment file format updaemon writes. Inside double quotes only
    /// <c>\ " $ `</c> are escaped (with a backslash), which systemd's <c>EnvironmentFile=</c> and
    /// <c>/bin/sh</c> both read the same way. Values must pass <see cref="SafeText"/>.
    /// </summary>
    public static class EnvFile
    {
        public static string Format(IEnumerable<KeyValuePair<string, string>> variables)
        {
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string, string> variable in variables.OrderBy(v => v.Key, StringComparer.Ordinal))
            {
                if (!SafeText.IsSafe(variable.Value))
                {
                    throw new ArgumentException($"The value of {variable.Key} contains a control character, such as a line break, which updaemon cannot store.");
                }

                builder.Append(variable.Key).Append("=\"");
                foreach (char c in variable.Value)
                {
                    if (c is '\\' or '"' or '$' or '`')
                    {
                        builder.Append('\\');
                    }

                    builder.Append(c);
                }

                builder.Append("\"\n");
            }

            return builder.ToString();
        }

        /// <summary>Reads a file written by <see cref="Format"/>.</summary>
        public static Dictionary<string, string> Parse(string content)
        {
            Dictionary<string, string> variables = new Dictionary<string, string>();
            foreach (string line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0 || line.Length < separator + 3 || line[separator + 1] != '"' || line[^1] != '"')
                {
                    continue;
                }

                string quoted = line.Substring(separator + 2, line.Length - separator - 3);
                StringBuilder value = new StringBuilder();
                for (int i = 0; i < quoted.Length; i++)
                {
                    if (quoted[i] == '\\' && i + 1 < quoted.Length)
                    {
                        i++;
                    }

                    value.Append(quoted[i]);
                }

                variables[line.Substring(0, separator)] = value.ToString();
            }

            return variables;
        }

        /// <summary>Writes the file with the given mode from creation, replacing any existing file atomically.</summary>
        public static async Task WriteAsync(string path, IEnumerable<KeyValuePair<string, string>> variables, UnixFileMode mode, CancellationToken cancellationToken = default)
        {
            await AtomicFile.WriteAsync(path, Format(variables), mode, cancellationToken);
        }

        public static async Task<Dictionary<string, string>> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, string>();
            }

            return Parse(await File.ReadAllTextAsync(path, cancellationToken));
        }
    }
}
