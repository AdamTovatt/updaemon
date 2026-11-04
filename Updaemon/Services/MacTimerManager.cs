using System.Xml.Linq;
using Updaemon.Configuration;
using Updaemon.Conversion;
using Updaemon.Interfaces;
using Updaemon.IO;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// macOS implementation of <see cref="ITimerManager"/> backed by a single launchd
    /// LaunchDaemon (system scope) or LaunchAgent (user scope) plist that runs <c>updaemon update</c> every <c>StartInterval</c> seconds.
    /// Constructed only on macOS by Program.cs.
    /// </summary>
    public class MacTimerManager : ITimerManager
    {
        /// <summary>Updaemon 0.x's timer label, in the system domain.</summary>
        private const string ZeroXLabel = "com.updaemon.timer";

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

        public async Task SetTimerAsync(TimeSpan interval, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            int seconds = ClampToInt(interval.TotalSeconds);

            // launchd refuses a plist that others can write; 0644 is what it expects.
            await AtomicFile.WriteAsync(_plistPath, BuildPlist(_label, _scope.UpdaemonCommandPath, seconds, _scope.UpdateLogPath), AtomicFile.Readable, cancellationToken);
            await Launchd.LoadAsync(_scope, _label, _plistPath, _outputWriter, cancellationToken);
        }

        /// <summary>Unloads the job and deletes its plist: launchd loads any plist left in the directory at the next boot or login.</summary>
        public async Task RemoveTimerAsync(ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            await Launchd.UnloadAsync(_scope, _label, cancellationToken);
            File.Delete(_plistPath);
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

            int? seconds = ReadStartInterval(await File.ReadAllTextAsync(_plistPath, cancellationToken));
            return seconds == null ? null : FormatInterval(seconds.Value);
        }

        public async Task<ZeroXTimer?> FindZeroXTimerAsync(CancellationToken cancellationToken = default)
        {
            string path = Path.Combine(_scope.UnitDirectory, ZeroXLabel + ".plist");
            if (!_scope.IsSystem || !File.Exists(path))
            {
                return null;
            }

            int? seconds = ReadStartInterval(await File.ReadAllTextAsync(path, cancellationToken));
            ProcessRunner.Result loaded = await ProcessRunner.RunAsync("launchctl", new[] { "print", Launchd.Target(_scope, ZeroXLabel) }, cancellationToken);
            return new ZeroXTimer(path, loaded.Success, seconds == null ? null : TimeSpan.FromSeconds(seconds.Value));
        }

        public async Task RemoveZeroXTimerAsync(ZeroXTimer timer, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            // 0.x's label differs from this updaemon's, so its job would otherwise run beside the new one.
            await Launchd.UnloadAsync(_scope, ZeroXLabel, cancellationToken);
            File.Delete(timer.FilePath);
        }

        internal static int? ReadStartInterval(string plist)
        {
            XElement? interval = Launchd.ReadPlist(plist)?.GetValueOrDefault("StartInterval");
            return interval?.Name == "integer" && int.TryParse(interval.Value, out int seconds) ? seconds : null;
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

        internal static string BuildPlist(string label, string updaemonPath, int intervalSeconds, string logPath)
        {
            return new Launchd.Plist()
                .String("Label", label)
                .Strings("ProgramArguments", new[] { updaemonPath, "update" })
                .Integer("StartInterval", intervalSeconds)
                .Boolean("RunAtLoad", false)
                .String("StandardOutPath", logPath)
                .String("StandardErrorPath", logPath)
                .ToString();
        }
    }
}
