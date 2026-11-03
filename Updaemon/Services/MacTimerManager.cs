using System.Text.RegularExpressions;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.IO;

namespace Updaemon.Services
{
    /// <summary>
    /// macOS implementation of <see cref="ITimerManager"/> backed by a single launchd
    /// LaunchDaemon (system scope) or LaunchAgent (user scope) plist that runs <c>updaemon update</c> every <c>StartInterval</c> seconds.
    /// Constructed only on macOS by Program.cs.
    /// </summary>
    public partial class MacTimerManager : ITimerManager
    {
        [GeneratedRegex("<key>StartInterval</key>\\s*<integer>(\\d+)</integer>", RegexOptions.IgnoreCase)]
        private static partial Regex StartIntervalRegex();

        private readonly IOutputWriter _outputWriter;
        private readonly UpdaemonScope _scope;
        private readonly string _plistPath;
        private readonly string _label;

        public MacTimerManager(IOutputWriter outputWriter, UpdaemonScope scope)
        {
            _outputWriter = outputWriter;
            _scope = scope;
            _label = scope.TimerLabel;
            _plistPath = Path.Combine(scope.UnitDirectory, _label + ".plist");
        }

        public async Task SetTimerAsync(TimeSpan interval, CancellationToken cancellationToken = default)
        {
            int seconds = ClampToInt(interval.TotalSeconds);

            // launchd refuses a plist that others can write; 0644 is what it expects.
            await AtomicFile.WriteAsync(_plistPath, BuildPlist(_label, _scope.UpdaemonCommandPath, seconds), AtomicFile.Readable, cancellationToken);
            await Launchd.LoadAsync(_scope, _label, _plistPath, _outputWriter, cancellationToken);
        }

        /// <summary>
        /// Disables the timer. Mirrors systemd semantics: the underlying job is unloaded,
        /// but the plist file is preserved on disk so its previous interval is still queryable
        /// via <see cref="GetCurrentIntervalAsync"/> and a re-enable doesn't have to re-derive it.
        /// </summary>
        public async Task DisableTimerAsync(CancellationToken cancellationToken = default)
        {
            await Launchd.UnloadAsync(_scope, _label, cancellationToken);
        }

        public async Task<bool> IsTimerEnabledAsync(CancellationToken cancellationToken = default)
        {
            ProcessRunner.Result result = await ProcessRunner.RunAsync("launchctl", new[] { "print", Launchd.Target(_scope, _label) }, cancellationToken);
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
            return new Launchd.Plist()
                .String("Label", label)
                .Strings("ProgramArguments", new[] { updaemonPath, "update" })
                .Integer("StartInterval", intervalSeconds)
                .Boolean("RunAtLoad", false)
                .ToString();
        }
    }
}
