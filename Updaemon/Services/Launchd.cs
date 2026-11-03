using System.Security;
using System.Text;
using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// What app services and the update timer share on macOS: writing a launchd plist, and loading
    /// one into the scope's launchd domain.
    /// </summary>
    public static class Launchd
    {
        /// <summary>
        /// Unloads the job if it is loaded, then bootstraps the plist. launchd reads the plist only when it
        /// loads a job, so this is the only way a changed plist takes effect.
        /// </summary>
        public static async Task LoadAsync(UpdaemonScope scope, string label, string plistPath, IOutputWriter outputWriter, CancellationToken cancellationToken)
        {
            if (scope.IsSystem)
            {
                // LaunchDaemons must be owned by root:wheel. Root normally creates files that way; be explicit.
                ProcessRunner.Result chown = await ProcessRunner.RunAsync("chown", new[] { "root:wheel", plistPath }, cancellationToken);
                if (!chown.Success)
                {
                    outputWriter.WriteError($"Warning: chown root:wheel on {plistPath} failed (exit {chown.ExitCode}): {chown.StdErr.Trim()}");
                }
            }

            await UnloadAsync(scope, label, cancellationToken);
            await ProcessRunner.RunAndCheckAsync("launchctl", new[] { "bootstrap", scope.LaunchdDomain, plistPath }, cancellationToken);
        }

        /// <summary>Unloads the job, which also stops it. Quiet when it is not loaded.</summary>
        public static async Task UnloadAsync(UpdaemonScope scope, string label, CancellationToken cancellationToken)
        {
            await ProcessRunner.RunAsync("launchctl", new[] { "bootout", Target(scope, label) }, cancellationToken);
        }

        public static string Target(UpdaemonScope scope, string label) => $"{scope.LaunchdDomain}/{label}";

        /// <summary>Builds a property list; every value is XML-escaped.</summary>
        public sealed class Plist
        {
            private readonly StringBuilder _entries = new StringBuilder();

            public Plist String(string key, string value)
            {
                Key(key);
                _entries.AppendLine($"    <string>{Escape(value)}</string>");
                return this;
            }

            public Plist Strings(string key, IEnumerable<string> values)
            {
                Key(key);
                _entries.AppendLine("    <array>");
                foreach (string value in values)
                {
                    _entries.AppendLine($"        <string>{Escape(value)}</string>");
                }

                _entries.AppendLine("    </array>");
                return this;
            }

            public Plist Dictionary(string key, IEnumerable<KeyValuePair<string, string>> values)
            {
                Key(key);
                _entries.AppendLine("    <dict>");
                foreach (KeyValuePair<string, string> value in values.OrderBy(v => v.Key, StringComparer.Ordinal))
                {
                    _entries.AppendLine($"        <key>{Escape(value.Key)}</key>");
                    _entries.AppendLine($"        <string>{Escape(value.Value)}</string>");
                }

                _entries.AppendLine("    </dict>");
                return this;
            }

            public Plist Boolean(string key, bool value)
            {
                Key(key);
                _entries.AppendLine(value ? "    <true/>" : "    <false/>");
                return this;
            }

            public Plist Integer(string key, int value)
            {
                Key(key);
                _entries.AppendLine($"    <integer>{value}</integer>");
                return this;
            }

            public override string ToString()
            {
                return $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                    <plist version="1.0">
                    <dict>
                    {_entries}</dict>
                    </plist>

                    """;
            }

            private void Key(string key) => _entries.AppendLine($"    <key>{Escape(key)}</key>");

            private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
        }
    }
}
