using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// What app services and the update timer share on macOS: writing and reading a launchd plist,
    /// and loading one into the scope's launchd domain.
    /// </summary>
    public static class Launchd
    {
        /// <summary>
        /// Unloads the job if it is loaded, then bootstraps the plist. launchd reads the plist only when it
        /// loads a job, so this is the only way a changed plist takes effect.
        /// </summary>
        public static async Task LoadAsync(UpdaemonScope scope, string label, string plistPath, IOutputWriter outputWriter, CancellationToken cancellationToken)
        {
            // Every plist updaemon writes logs under the scope's log directory, and launchd silently
            // drops the output when the directory is missing.
            try
            {
                Directory.CreateDirectory(scope.LogDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                outputWriter.WriteError($"Warning: could not create the log directory {scope.LogDirectory}: {ex.Message}");
            }

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

        /// <summary>A plist's top-level dictionary, its values by key; null when the text is not such a plist.</summary>
        public static Dictionary<string, XElement>? ReadPlist(string text)
        {
            XDocument document;
            try
            {
                // Indentation means nothing in a plist, so values compare equal however they are laid out.
                XmlReaderSettings readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreWhitespace = true };
                using (XmlReader reader = XmlReader.Create(new StringReader(text), readerSettings))
                {
                    document = XDocument.Load(reader);
                }
            }
            catch (XmlException)
            {
                return null;
            }

            XElement? dictionary = document.Root?.Name == "plist" ? document.Root.Elements().SingleOrDefault() : null;
            if (dictionary?.Name != "dict")
            {
                return null;
            }

            Dictionary<string, XElement> entries = new Dictionary<string, XElement>();
            List<XElement> elements = dictionary.Elements().ToList();
            for (int i = 0; i < elements.Count; i += 2)
            {
                if (elements[i].Name != "key" || i + 1 >= elements.Count || !entries.TryAdd(elements[i].Value, elements[i + 1]))
                {
                    return null;
                }
            }

            return entries;
        }

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
