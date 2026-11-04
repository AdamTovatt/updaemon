using System.Text.RegularExpressions;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// macOS implementation of <see cref="IServiceManager"/> backed by launchctl: LaunchDaemons in
    /// the <c>system</c> domain for the system scope, LaunchAgents in the user's <c>gui/&lt;uid&gt;</c>
    /// domain otherwise. launchd reads a plist only when it loads the job, so starting and
    /// restarting always reload it: that is how changed settings and arguments take effect.
    /// </summary>
    public partial class MacServiceManager : IServiceManager
    {
        // launchctl print emits a multi-line block; "state = running" is the contract for an alive job.
        private const string StateRunningMarker = "state = running";

        private readonly UpdaemonScope _scope;
        private readonly IOutputWriter _outputWriter;

        public MacServiceManager(UpdaemonScope scope, IOutputWriter outputWriter)
        {
            _scope = scope;
            _outputWriter = outputWriter;
        }

        /// <summary>Loads the plist; <c>RunAtLoad</c> starts the job.</summary>
        public Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => LoadAsync(serviceName, cancellationToken);

        /// <summary>Unloads the job: with <c>KeepAlive</c>, launchd would relaunch a merely killed process.</summary>
        public Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => Launchd.UnloadAsync(_scope, Label(serviceName), cancellationToken);

        /// <summary>Unloads and loads the plist again, whether or not the job runs.</summary>
        public Task RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => LoadAsync(serviceName, cancellationToken);

        public Task<bool> IsEnabledAsync(string serviceName, CancellationToken cancellationToken = default)
            => Task.FromResult(File.Exists(_scope.GetUnitFilePath(serviceName)));

        /// <summary>Nothing to do: launchd loads every plist in the scope's directory at boot or login.</summary>
        public Task EnableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DisableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => Launchd.UnloadAsync(_scope, Label(serviceName), cancellationToken);

        public async Task<ServiceState> GetStateAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            ProcessRunner.Result result = await ProcessRunner.RunAsync("launchctl", new[] { "print", Launchd.Target(_scope, Label(serviceName)) }, cancellationToken);
            return result.Success ? ParseState(result.StdOut) : new ServiceState(false, 0);
        }

        public Task<int> ShowLogsAsync(string serviceName, bool follow, CancellationToken cancellationToken = default)
        {
            string[] files = new[] { _scope.GetOutputLogPath(serviceName), _scope.GetErrorLogPath(serviceName) }.Where(File.Exists).ToArray();
            if (files.Length == 0)
            {
                _outputWriter.WriteLine($"{serviceName} has not written any log yet.");
                return Task.FromResult(0);
            }

            return ProcessRunner.RunAttachedAsync("tail", TailArguments(files, follow), cancellationToken);
        }

        internal static string[] TailArguments(string[] files, bool follow)
        {
            return [.. (follow ? new[] { "-f" } : Array.Empty<string>()), "-n", "100", .. files];
        }

        /// <summary>Reads <c>launchctl print</c>: "state = running" and the "runs = N" launch count.</summary>
        internal static ServiceState ParseState(string output)
        {
            Match runs = RunsRegex().Match(output);
            return new ServiceState(
                output.Contains(StateRunningMarker, StringComparison.Ordinal),
                runs.Success ? int.Parse(runs.Groups[1].Value) : 0);
        }

        [GeneratedRegex(@"^\s*runs = (\d+)\s*$", RegexOptions.Multiline)]
        private static partial Regex RunsRegex();

        public async Task<bool> ServiceExistsAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            if (File.Exists(_scope.GetUnitFilePath(serviceName)))
            {
                return true;
            }

            ProcessRunner.Result result = await ProcessRunner.RunAsync("launchctl", new[] { "print", Launchd.Target(_scope, Label(serviceName)) }, cancellationToken);
            return result.Success;
        }

        /// <summary>Nothing to do: every start and restart reloads the plist.</summary>
        public Task DaemonReloadAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ServicesStopAtLogoutAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public async Task<ReachableServiceManager> EnsureReachableAsync(CancellationToken cancellationToken = default)
        {
            if (_scope.IsSystem)
            {
                return new ReachableServiceManager();
            }

            ProcessRunner.Result result = await ProcessRunner.RunAsync("launchctl", new[] { "print", _scope.LaunchdDomain }, cancellationToken);
            return result.Success ? new ReachableServiceManager() : throw Unreachable(_scope.UserName!, _scope.LaunchdDomain);
        }

        /// <summary>A user's launchd domain exists only while that user is logged in to the Mac's desktop.</summary>
        internal static ServiceManagerUnreachableException Unreachable(string user, string domain) => new ServiceManagerUnreachableException(
            $"Cannot reach launchd's {domain} domain for {user}, which exists only while {user} is logged in to the Mac's desktop. " +
            $"Log in as {user} on the Mac itself, then try again.");

        private string Label(string serviceName) => _scope.GetServiceLabel(serviceName);

        private async Task LoadAsync(string serviceName, CancellationToken cancellationToken)
        {
            string plistPath = _scope.GetUnitFilePath(serviceName);
            if (!File.Exists(plistPath))
            {
                throw new InvalidOperationException($"Plist not found at '{plistPath}'. Cannot load '{serviceName}'.");
            }

            await Launchd.LoadAsync(_scope, Label(serviceName), plistPath, _outputWriter, cancellationToken);
        }
    }
}
