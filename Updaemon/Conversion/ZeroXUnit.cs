using System.Xml.Linq;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Conversion
{
    /// <summary>What reading a 0.x service unit found: the unit as 0.x wrote it, or a hand edit.</summary>
    public abstract record ZeroXUnitReading
    {
        private ZeroXUnitReading()
        {
        }

        /// <summary>The unit runs the executable in the app's <c>current</c> directory, with these settings.</summary>
        public sealed record AsWritten(string ExecutableName, IReadOnlyDictionary<string, string> Settings) : ZeroXUnitReading;

        /// <summary>The unit was edited by hand; <paramref name="Problem"/> names the line in question.</summary>
        public sealed record HandEdited(string Problem) : ZeroXUnitReading;
    }

    /// <summary>
    /// Reads a 0.x service unit or plist by comparing it with the template 0.x generated it from.
    /// <c>Environment=</c> lines (plist: <c>EnvironmentVariables</c>) are read as settings; any other
    /// difference is a hand edit updaemon cannot tell the meaning of, so the unit is not converted.
    /// </summary>
    public static class ZeroXUnit
    {
        private const string EnvironmentPrefix = "Environment=";

        // 0.x's embedded service.template.
        private const string SystemdTemplate = """
            [Unit]
            Description={DESCRIPTION}
            After=network.target

            [Service]
            Type=simple
            WorkingDirectory={WORKING_DIRECTORY}
            ExecStart={WORKING_DIRECTORY}/{EXECUTABLE_NAME}
            Restart=always
            RestartSec=10
            StandardOutput=journal
            StandardError=journal
            SyslogIdentifier={SERVICE_NAME}

            [Install]
            WantedBy=multi-user.target
            """;

        // 0.x's embedded launchd.plist.template.
        private const string PlistTemplate = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>com.updaemon.{SERVICE_NAME}</string>
                <key>WorkingDirectory</key>
                <string>{WORKING_DIRECTORY}</string>
                <key>ProgramArguments</key>
                <array>
                    <string>{WORKING_DIRECTORY}/{EXECUTABLE_NAME}</string>
                </array>
                <key>RunAtLoad</key>
                <true/>
                <key>KeepAlive</key>
                <true/>
                <key>ThrottleInterval</key>
                <integer>10</integer>
                <key>StandardOutPath</key>
                <string>/var/log/updaemon/{SERVICE_NAME}.log</string>
                <key>StandardErrorPath</key>
                <string>/var/log/updaemon/{SERVICE_NAME}.err.log</string>
            </dict>
            </plist>
            """;

        /// <summary>Reads the unit of 0.x app <paramref name="appName"/>, whose <c>current</c> link is <paramref name="currentLink"/>.</summary>
        public static ZeroXUnitReading Read(string text, string appName, string currentLink, bool isMacOS)
        {
            return isMacOS ? ReadPlist(text, appName, currentLink) : ReadSystemd(text, appName, currentLink);
        }

        private static ZeroXUnitReading ReadSystemd(string text, string appName, string currentLink)
        {
            Dictionary<string, string> settings = new Dictionary<string, string>();
            List<string> lines = new List<string>();
            foreach (string line in MeaningfulLines(text))
            {
                if (!line.StartsWith(EnvironmentPrefix, StringComparison.Ordinal))
                {
                    lines.Add(line);
                }
                else if (!TryReadEnvironment(line.Substring(EnvironmentPrefix.Length), settings))
                {
                    return new ZeroXUnitReading.HandEdited($"the line '{line}' is not one updaemon can read as settings");
                }
            }

            string execStartPrefix = $"ExecStart={currentLink}/";
            string? executableName = lines.FirstOrDefault(line => line.StartsWith(execStartPrefix, StringComparison.Ordinal))?.Substring(execStartPrefix.Length);
            if (executableName == null || !IsFileName(executableName))
            {
                return new ZeroXUnitReading.HandEdited($"it has no 'ExecStart={currentLink}/<executable>' line");
            }

            string expected = Render(SystemdTemplate, appName, $"{appName} service managed by updaemon", currentLink, executableName);
            List<string> expectedLines = MeaningfulLines(expected).ToList();
            for (int i = 0; i < Math.Max(lines.Count, expectedLines.Count); i++)
            {
                if (i >= lines.Count)
                {
                    return new ZeroXUnitReading.HandEdited($"the line '{expectedLines[i]}' is missing");
                }

                if (i >= expectedLines.Count || lines[i] != expectedLines[i])
                {
                    return new ZeroXUnitReading.HandEdited($"the line '{lines[i]}' differs from what updaemon 0.x wrote");
                }
            }

            return new ZeroXUnitReading.AsWritten(executableName, settings);
        }

        private static ZeroXUnitReading ReadPlist(string text, string appName, string currentLink)
        {
            Dictionary<string, XElement>? actual = Launchd.ReadPlist(text);
            if (actual == null)
            {
                return new ZeroXUnitReading.HandEdited("it is not a plist updaemon can read");
            }

            Dictionary<string, string> settings = new Dictionary<string, string>();
            if (actual.Remove("EnvironmentVariables", out XElement? environment) && !TryReadEnvironmentDictionary(environment, settings))
            {
                return new ZeroXUnitReading.HandEdited("its EnvironmentVariables are not all names with text values");
            }

            string programPrefix = currentLink + "/";
            string? program = actual.GetValueOrDefault("ProgramArguments")?.Elements("string").FirstOrDefault()?.Value;
            string? executableName = program != null && program.StartsWith(programPrefix, StringComparison.Ordinal) ? program.Substring(programPrefix.Length) : null;
            if (executableName == null || !IsFileName(executableName))
            {
                return new ZeroXUnitReading.HandEdited($"its ProgramArguments do not run '{currentLink}/<executable>'");
            }

            string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
            Dictionary<string, XElement> expected = Launchd.ReadPlist(Render(PlistTemplate, Escape(appName), string.Empty, Escape(currentLink), Escape(executableName)))!;
            foreach (KeyValuePair<string, XElement> entry in actual)
            {
                if (!expected.TryGetValue(entry.Key, out XElement? expectedValue) || !XNode.DeepEquals(entry.Value, expectedValue))
                {
                    return new ZeroXUnitReading.HandEdited($"its '{entry.Key}' differs from what updaemon 0.x wrote");
                }
            }

            if (expected.Keys.FirstOrDefault(key => !actual.ContainsKey(key)) is string missing)
            {
                return new ZeroXUnitReading.HandEdited($"its '{missing}' is missing");
            }

            return new ZeroXUnitReading.AsWritten(executableName, settings);
        }

        private static string Render(string template, string serviceName, string description, string workingDirectory, string executableName)
        {
            return template
                .Replace("{SERVICE_NAME}", serviceName)
                .Replace("{DESCRIPTION}", description)
                .Replace("{WORKING_DIRECTORY}", workingDirectory)
                .Replace("{EXECUTABLE_NAME}", executableName);
        }

        /// <summary>Trimmed lines, without blank lines and comments, which change nothing.</summary>
        private static IEnumerable<string> MeaningfulLines(string text)
        {
            return text.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith(';'));
        }

        private static bool IsFileName(string name) => name.Length > 0 && !name.Contains('/') && !name.Any(char.IsWhiteSpace) && SafeText.IsSafe(name);

        /// <summary>Reads one <c>Environment=</c> value: <c>KEY=value</c> words (see <see cref="SystemdSyntax.SplitWords"/>), at least one.</summary>
        private static bool TryReadEnvironment(string value, Dictionary<string, string> settings)
        {
            IReadOnlyList<string>? assignments = SystemdSyntax.SplitWords(value);
            if (assignments == null || assignments.Count == 0)
            {
                return false;
            }

            foreach (string assignment in assignments)
            {
                int separator = assignment.IndexOf('=');
                if (separator <= 0 || !TryAddSetting(assignment.Substring(0, separator), assignment.Substring(separator + 1), settings))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadEnvironmentDictionary(XElement dictionary, Dictionary<string, string> settings)
        {
            if (dictionary.Name != "dict")
            {
                return false;
            }

            List<XElement> elements = dictionary.Elements().ToList();
            for (int i = 0; i < elements.Count; i += 2)
            {
                if (elements[i].Name != "key" || i + 1 >= elements.Count || elements[i + 1].Name != "string"
                    || !TryAddSetting(elements[i].Value, elements[i + 1].Value, settings))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryAddSetting(string key, string value, Dictionary<string, string> settings)
        {
            if (!AppManifest.IsValidSettingKey(key) || key == AppManifest.DataDirectoryVariable || !SafeText.IsSafe(value))
            {
                return false;
            }

            settings[key] = value;
            return true;
        }
    }
}
