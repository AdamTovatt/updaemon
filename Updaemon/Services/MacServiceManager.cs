using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// macOS implementation of <see cref="IServiceManager"/> backed by launchctl: LaunchDaemons in
    /// the <c>system</c> domain for the system scope, LaunchAgents in the user's <c>gui/&lt;uid&gt;</c>
    /// domain otherwise. launchd reads a plist only when it loads the job, so starting and
    /// restarting always reload it: that is how changed settings and arguments take effect.
    /// </summary>
    public class MacServiceManager : IServiceManager
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

        public Task RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => LoadAsync(serviceName, cancellationToken);

        /// <summary>Nothing to do: launchd loads every plist in the scope's directory at boot or login.</summary>
        public Task EnableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DisableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => Launchd.UnloadAsync(_scope, Label(serviceName), cancellationToken);

        public async Task<bool> IsServiceRunningAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            ProcessRunner.Result result = await ProcessRunner.RunAsync("launchctl", new[] { "print", Launchd.Target(_scope, Label(serviceName)) }, cancellationToken);
            return result.Success && result.StdOut.Contains(StateRunningMarker, StringComparison.Ordinal);
        }

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

        private string Label(string serviceName) => _scope.GetServiceLabel(serviceName);

        private async Task LoadAsync(string serviceName, CancellationToken cancellationToken)
        {
            string plistPath = _scope.GetUnitFilePath(serviceName);
            if (!File.Exists(plistPath))
            {
                throw new InvalidOperationException($"Plist not found at '{plistPath}'. Cannot load '{serviceName}'.");
            }

            // The plist's StandardOutPath / StandardErrorPath point under the scope's log directory.
            // If the directory doesn't exist, launchd silently drops logs — make sure it's there.
            try
            {
                Directory.CreateDirectory(_scope.LogDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _outputWriter.WriteError($"Warning: could not create log directory {_scope.LogDirectory}: {ex.Message}");
            }

            await Launchd.LoadAsync(_scope, Label(serviceName), plistPath, _outputWriter, cancellationToken);
        }
    }
}
