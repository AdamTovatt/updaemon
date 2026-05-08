using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// macOS implementation of <see cref="IServiceManager"/> backed by launchd / launchctl.
    /// Manages LaunchDaemons under <see cref="PlatformPaths.UnitFileDirectory"/>.
    /// Service labels follow reverse-DNS convention: com.updaemon.&lt;name&gt;.
    /// Constructed only on macOS by Program.cs; calling launchctl on other platforms will fail at runtime.
    /// </summary>
    public class MacServiceManager : IServiceManager
    {
        // launchctl print emits a multi-line block; "state = running" is the contract for an alive job.
        private const string StateRunningMarker = "state = running";

        private readonly IUnitFileManager _unitFileManager;
        private readonly IOutputWriter _outputWriter;

        public MacServiceManager(IUnitFileManager unitFileManager, IOutputWriter outputWriter)
        {
            _unitFileManager = unitFileManager;
            _outputWriter = outputWriter;
        }

        public Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => KickstartOrBootstrapAsync(serviceName, killExisting: false, cancellationToken);

        /// <summary>
        /// Stops a managed service. On launchd this is implemented as <c>bootout</c>, which
        /// also unloads the plist (because services with <c>KeepAlive=true</c> would otherwise
        /// be relaunched immediately by launchd). To re-start the service afterwards, call
        /// <see cref="StartServiceAsync"/> — it will re-bootstrap automatically.
        /// </summary>
        public async Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            string label = PlatformPaths.GetUnitLabel(serviceName);
            // bootout is best-effort: if the service isn't loaded, the call returns non-zero
            // and we treat that as "already stopped". No warning needed for that case.
            await ProcessRunner.RunAsync("launchctl", new[] { "bootout", $"system/{label}" }, cancellationToken);
        }

        public Task RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => KickstartOrBootstrapAsync(serviceName, killExisting: true, cancellationToken);

        public async Task EnableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await BootstrapAsync(serviceName, cancellationToken);
        }

        public async Task DisableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            string label = PlatformPaths.GetUnitLabel(serviceName);
            await ProcessRunner.RunAsync("launchctl", new[] { "bootout", $"system/{label}" }, cancellationToken);
        }

        public async Task<bool> IsServiceRunningAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            string label = PlatformPaths.GetUnitLabel(serviceName);
            ProcessRunner.Result result = await ProcessRunner.RunAsync(
                "launchctl",
                new[] { "print", $"system/{label}" },
                cancellationToken);

            if (!result.Success)
            {
                return false;
            }

            return result.StdOut.Contains(StateRunningMarker, StringComparison.Ordinal);
        }

        public Task<bool> ServiceExistsAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            string plistPath = _unitFileManager.GetUnitFilePath(serviceName);
            return Task.FromResult(File.Exists(plistPath));
        }

        public Task DaemonReloadAsync(CancellationToken cancellationToken = default)
        {
            // launchd reloads on each bootstrap; nothing to do here.
            return Task.CompletedTask;
        }

        private async Task KickstartOrBootstrapAsync(string serviceName, bool killExisting, CancellationToken cancellationToken)
        {
            string label = PlatformPaths.GetUnitLabel(serviceName);
            string[] args = killExisting
                ? new[] { "kickstart", "-k", $"system/{label}" }
                : new[] { "kickstart", $"system/{label}" };

            ProcessRunner.Result result = await ProcessRunner.RunAsync("launchctl", args, cancellationToken);
            if (!result.Success)
            {
                // Service might not be bootstrapped yet — fall through to bootstrap.
                await BootstrapAsync(serviceName, cancellationToken);
            }
        }

        private async Task BootstrapAsync(string serviceName, CancellationToken cancellationToken)
        {
            string label = PlatformPaths.GetUnitLabel(serviceName);
            string plistPath = _unitFileManager.GetUnitFilePath(serviceName);

            if (!File.Exists(plistPath))
            {
                throw new InvalidOperationException($"Plist not found at '{plistPath}'. Cannot bootstrap '{serviceName}'.");
            }

            // The plist's StandardOutPath / StandardErrorPath point under PlatformPaths.MacLogDirectory.
            // If the directory doesn't exist, launchd silently drops logs — make sure it's there.
            try
            {
                Directory.CreateDirectory(PlatformPaths.MacLogDirectory);
            }
            catch (Exception ex)
            {
                _outputWriter.WriteLine($"Warning: could not create log directory {PlatformPaths.MacLogDirectory}: {ex.Message}");
            }

            // Enforce root:wheel ownership on the plist. launchd inherits the file's owner;
            // this is normally already correct (root creates files as root:wheel) but be explicit.
            ProcessRunner.Result chown = await ProcessRunner.RunAsync("chown", new[] { "root:wheel", plistPath }, cancellationToken);
            if (!chown.Success)
            {
                _outputWriter.WriteLine($"Warning: chown root:wheel on {plistPath} failed (exit {chown.ExitCode}): {chown.StdErr.Trim()}");
            }

            // If already bootstrapped, bootstrap fails — bootout first (best effort).
            await ProcessRunner.RunAsync("launchctl", new[] { "bootout", $"system/{label}" }, cancellationToken);

            await ProcessRunner.RunAndCheckAsync("launchctl", new[] { "bootstrap", "system", plistPath }, cancellationToken);
        }
    }
}
