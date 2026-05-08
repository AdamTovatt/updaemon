using System.Text.RegularExpressions;
using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// macOS implementation of <see cref="ITimerManager"/> backed by a single launchd
    /// LaunchDaemon plist that runs <c>updaemon update</c> every <c>StartInterval</c> seconds.
    /// Constructed only on macOS by Program.cs.
    /// </summary>
    public partial class MacTimerManager : ITimerManager
    {
        [GeneratedRegex("<key>StartInterval</key>\\s*<integer>(\\d+)</integer>", RegexOptions.IgnoreCase)]
        private static partial Regex StartIntervalRegex();

        private readonly IOutputWriter _outputWriter;
        private readonly string _plistPath;
        private readonly string _label;
        private readonly string _updaemonExecutablePath;

        public MacTimerManager(IOutputWriter outputWriter)
            : this(
                outputWriter,
                Path.Combine(PlatformPaths.UnitFileDirectory, PlatformPaths.TimerUnitName + ".plist"),
                PlatformPaths.TimerUnitName,
                PlatformPaths.UpdaemonExecutablePath)
        {
        }

        public MacTimerManager(IOutputWriter outputWriter, string plistPath, string label, string updaemonExecutablePath)
        {
            _outputWriter = outputWriter;
            _plistPath = plistPath;
            _label = label;
            _updaemonExecutablePath = updaemonExecutablePath;
        }

        public async Task SetTimerAsync(TimeSpan interval, CancellationToken cancellationToken = default)
        {
            int seconds = ClampToInt(interval.TotalSeconds);

            string plistContent = BuildPlist(_label, _updaemonExecutablePath, seconds);
            await File.WriteAllTextAsync(_plistPath, plistContent, cancellationToken);

            // launchd refuses plists that aren't 0644 root:wheel.
            try
            {
                File.SetUnixFileMode(_plistPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }
            catch (Exception ex)
            {
                _outputWriter.WriteLine($"Warning: could not set permissions on {_plistPath}: {ex.Message}");
            }

            // Belt-and-suspenders: ensure root:wheel ownership. On macOS root's primary group
            // is already wheel so this is usually a no-op, but make it explicit.
            ProcessRunner.Result chown = await ProcessRunner.RunAsync("chown", new[] { "root:wheel", _plistPath }, cancellationToken);
            if (!chown.Success)
            {
                _outputWriter.WriteLine($"Warning: chown root:wheel on {_plistPath} failed (exit {chown.ExitCode}): {chown.StdErr.Trim()}");
            }

            // Re-bootstrap to pick up changes.
            await ProcessRunner.RunAsync("launchctl", new[] { "bootout", $"system/{_label}" }, cancellationToken);
            await ProcessRunner.RunAndCheckAsync("launchctl", new[] { "bootstrap", "system", _plistPath }, cancellationToken);
        }

        /// <summary>
        /// Disables the timer. Mirrors systemd semantics: the underlying job is unloaded,
        /// but the plist file is preserved on disk so its previous interval is still queryable
        /// via <see cref="GetCurrentIntervalAsync"/> and a re-enable doesn't have to re-derive it.
        /// </summary>
        public async Task DisableTimerAsync(CancellationToken cancellationToken = default)
        {
            await ProcessRunner.RunAsync("launchctl", new[] { "bootout", $"system/{_label}" }, cancellationToken);
        }

        public async Task<bool> IsTimerEnabledAsync(CancellationToken cancellationToken = default)
        {
            ProcessRunner.Result result = await ProcessRunner.RunAsync("launchctl", new[] { "print", $"system/{_label}" }, cancellationToken);
            return result.Success;
        }

        public async Task<string?> GetCurrentIntervalAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_plistPath))
            {
                return null;
            }

            string content = await File.ReadAllTextAsync(_plistPath, cancellationToken);
            Match match = StartIntervalRegex().Match(content);
            if (!match.Success)
            {
                return null;
            }

            if (!int.TryParse(match.Groups[1].Value, out int seconds))
            {
                return null;
            }

            return FormatInterval(seconds);
        }

        private static int ClampToInt(double value)
        {
            if (double.IsNaN(value) || value < 1) return 1;
            if (value > int.MaxValue) return int.MaxValue;
            return (int)value;
        }

        private static string FormatInterval(int seconds)
        {
            if (seconds % 3600 == 0)
            {
                return $"{seconds / 3600}h";
            }
            if (seconds % 60 == 0)
            {
                return $"{seconds / 60}m";
            }
            return $"{seconds}s";
        }

        internal static string BuildPlist(string label, string updaemonPath, int intervalSeconds)
        {
            return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>{label}</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{updaemonPath}</string>
                        <string>update</string>
                    </array>
                    <key>StartInterval</key>
                    <integer>{intervalSeconds}</integer>
                    <key>RunAtLoad</key>
                    <false/>
                </dict>
                </plist>

                """;
        }
    }
}
