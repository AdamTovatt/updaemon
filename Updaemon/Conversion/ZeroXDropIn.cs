using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Conversion
{
    /// <summary>
    /// A drop-in of a 0.x unit: a <c>.conf</c> file in <c>&lt;unit&gt;.service.d</c>, which systemd applies
    /// on top of the unit, in file name order. Converting leaves it in place, and the unit this updaemon
    /// writes has the same name, so systemd goes on applying all of it.
    /// </summary>
    /// <param name="Path">The drop-in file.</param>
    /// <param name="Lines">What it sets, as shown to the person: <c>User=</c> lines in full, the names of
    /// the variables it sets, and the names of its other lines. Values can be secrets, so are left out.</param>
    /// <param name="Variables">The names its <c>Environment=</c> lines set.</param>
    /// <param name="User">Its last <c>User=</c> in <c>[Service]</c>, which overrides the unit's; null when it sets none.</param>
    public sealed record ZeroXDropIn(string Path, IReadOnlyList<string> Lines, IReadOnlyList<string> Variables, ZeroXDropIn.UserLine? User)
    {
        /// <summary>A <c>User=</c> line: <paramref name="Name"/> is null for an empty one, which means root again.</summary>
        public sealed record UserLine(string? Name);

        /// <summary>The drop-in directory systemd reads for the unit at <paramref name="unitPath"/>.</summary>
        public static string DirectoryOf(string unitPath) => unitPath + ".d";

        /// <summary>The drop-ins of the unit at <paramref name="unitPath"/>, in the order systemd applies them.</summary>
        public static IReadOnlyList<string> FindPaths(string unitPath)
        {
            string directory = DirectoryOf(unitPath);
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(directory)
                .Where(path => path.EndsWith(".conf", StringComparison.Ordinal))
                .OrderBy(path => System.IO.Path.GetFileName(path), StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Reads the drop-in at <paramref name="path"/>, or returns why updaemon cannot carry it over.</summary>
        public static ZeroXDropInReading Read(string path, string text)
        {
            List<string> lines = new List<string>();
            List<string> variables = new List<string>();
            UserLine? user = null;
            string? section = null;
            foreach (string line in ZeroXUnit.MeaningfulLines(text))
            {
                if (line.StartsWith('['))
                {
                    section = line;
                    continue;
                }

                if (section == ZeroXUnit.ServiceSection && line.StartsWith(ZeroXUnit.UserPrefix, StringComparison.Ordinal))
                {
                    if (!ZeroXUnit.TryReadUser(line.Substring(ZeroXUnit.UserPrefix.Length), out string? name))
                    {
                        return new ZeroXDropInReading.Unreadable($"has the line '{line}', which is not one updaemon can read as the user it runs as");
                    }

                    user = new UserLine(name);
                    lines.Add(line);
                }
                else if (section == ZeroXUnit.ServiceSection && line.StartsWith(ZeroXUnit.EnvironmentPrefix, StringComparison.Ordinal))
                {
                    // Its values are not shown, so neither is the line.
                    IReadOnlyList<string>? names = ReadVariableNames(line.Substring(ZeroXUnit.EnvironmentPrefix.Length));
                    if (names == null)
                    {
                        return new ZeroXDropInReading.Unreadable("has an Environment= line updaemon cannot read");
                    }

                    if (names.Contains(AppManifest.DataDirectoryVariable))
                    {
                        return new ZeroXDropInReading.Unreadable($"sets {AppManifest.DataDirectoryVariable}, which updaemon sets for every app");
                    }

                    variables.AddRange(names);
                    lines.Add($"the variable{(names.Count == 1 ? string.Empty : "s")} {string.Join(", ", names)}");
                }
                else
                {
                    int separator = line.IndexOf('=');
                    lines.Add(separator > 0 ? line.Substring(0, separator) : line);
                }
            }

            return new ZeroXDropInReading.Read(new ZeroXDropIn(path, lines, variables, user));
        }

        /// <summary>
        /// The names an <c>Environment=</c> value sets, or null when it cannot be read. An empty one is
        /// refused too: it clears what the unit set, which converted settings would set again.
        /// </summary>
        private static IReadOnlyList<string>? ReadVariableNames(string value)
        {
            IReadOnlyList<string>? assignments = SystemdSyntax.SplitWords(value);
            if (assignments == null || assignments.Count == 0)
            {
                return null;
            }

            List<string> names = new List<string>();
            foreach (string assignment in assignments)
            {
                int separator = assignment.IndexOf('=');
                if (separator <= 0)
                {
                    return null;
                }

                names.Add(assignment.Substring(0, separator));
            }

            return names;
        }
    }

    /// <summary>What reading a drop-in of a 0.x unit found.</summary>
    public abstract record ZeroXDropInReading
    {
        private ZeroXDropInReading()
        {
        }

        public sealed record Read(ZeroXDropIn DropIn) : ZeroXDropInReading;

        /// <summary><paramref name="Problem"/> says what in it updaemon cannot carry over, following "the drop-in".</summary>
        public sealed record Unreadable(string Problem) : ZeroXDropInReading;
    }
}
