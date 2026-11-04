using System.ComponentModel;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Linux implementation of <see cref="IServiceManager"/> backed by systemctl: the system
    /// manager in the system scope, the user's own manager (<c>systemctl --user</c>) otherwise.
    /// </summary>
    public class LinuxServiceManager : IServiceManager
    {
        private readonly UpdaemonScope _scope;

        public LinuxServiceManager(UpdaemonScope scope)
        {
            _scope = scope;
        }

        public Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => SystemctlAsync(cancellationToken, "start", serviceName);

        public Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => SystemctlAsync(cancellationToken, "stop", serviceName);

        public async Task RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            // A unit that crashed too often in a row is failed and refuses to start until this clears it.
            await ProcessRunner.RunAsync("systemctl", Arguments("reset-failed", serviceName), cancellationToken);
            await SystemctlAsync(cancellationToken, "restart", serviceName);
        }

        public async Task<bool> IsEnabledAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            return await IsUnitEnabledAsync(_scope, serviceName + ".service", cancellationToken)
                ?? throw new InvalidOperationException($"systemd has no unit {serviceName}.service.");
        }

        /// <summary>
        /// Whether systemd starts <paramref name="unit"/> by itself; null when it has no such unit. Throws
        /// when systemctl cannot tell, as without a reachable manager: reading that as disabled would skip
        /// a restart in silence.
        /// </summary>
        internal static async Task<bool?> IsUnitEnabledAsync(UpdaemonScope scope, string unit, CancellationToken cancellationToken)
        {
            ProcessRunner.Result result = await ProcessRunner.RunAsync("systemctl", [.. scope.SystemdScopeArguments, "is-enabled", unit], cancellationToken);
            return ReadIsEnabled(unit, result);
        }

        /// <summary>Reads <c>systemctl is-enabled</c>, as <see cref="IsUnitEnabledAsync"/> describes.</summary>
        internal static bool? ReadIsEnabled(string unit, ProcessRunner.Result result)
        {
            string state = result.StdOut.Trim();
            return state switch
            {
                "enabled" or "enabled-runtime" => true,
                "disabled" => false,
                // Newer systemd says so; older ones only print the error.
                "not-found" => null,
                "" when result.StdErr.Contains("No such file or directory", StringComparison.Ordinal) => null,
                _ => throw new InvalidOperationException($"'systemctl is-enabled {unit}' could not tell whether it is enabled: {$"{state} {result.StdErr}".Trim()}"),
            };
        }

        public Task EnableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => SystemctlAsync(cancellationToken, "enable", serviceName);

        public Task DisableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => SystemctlAsync(cancellationToken, "disable", serviceName);

        public Task DaemonReloadAsync(CancellationToken cancellationToken = default)
            => SystemctlAsync(cancellationToken, "daemon-reload");

        public async Task<ServiceState> GetStateAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            ProcessRunner.Result result = await ProcessRunner.RunAndCheckAsync(
                "systemctl", Arguments("show", "--property=ActiveState,NRestarts", serviceName), cancellationToken);
            return ParseState(result.StdOut);
        }

        public Task<int> ShowLogsAsync(string serviceName, bool follow, CancellationToken cancellationToken = default)
        {
            return ProcessRunner.RunAttachedAsync("journalctl", JournalctlArguments(_scope, serviceName, follow), cancellationToken);
        }

        public async Task<bool> ServicesStopAtLogoutAsync(CancellationToken cancellationToken = default)
        {
            if (_scope.IsSystem)
            {
                return false;
            }

            try
            {
                ProcessRunner.Result result = await ProcessRunner.RunAsync("loginctl", new[] { "show-user", Environment.UserName, "-p", "Linger" }, cancellationToken);
                return !LingerIsOn(result.StdOut);
            }
            catch (Win32Exception)
            {
                // No loginctl: nothing says lingering is on.
                return true;
            }
        }

        public async Task<ReachableServiceManager> EnsureReachableAsync(CancellationToken cancellationToken = default)
        {
            if (_scope.IsSystem)
            {
                return new ReachableServiceManager();
            }

            ProcessRunner.Result result;
            try
            {
                result = await ProcessRunner.RunAsync("systemctl", Arguments("show-environment"), cancellationToken);
            }
            catch (Win32Exception)
            {
                throw Unreachable(_scope.UserName!, "systemctl was not found");
            }

            return result.Success ? new ReachableServiceManager() : throw Unreachable(_scope.UserName!, FirstLine(result.StdErr) ?? $"exit {result.ExitCode}");
        }

        /// <summary>
        /// The user manager is missing: it runs while the user is logged in, and from boot once lingering
        /// is on. A shell from su or sudo -u reaches a running one through <see cref="UserRuntimeDirectory"/>.
        /// </summary>
        internal static ServiceManagerUnreachableException Unreachable(string user, string problem) => new ServiceManagerUnreachableException(
            $"Cannot reach systemd's user manager for {user} ({problem.TrimEnd('.')}). It runs while {user} is logged in; " +
            $"to have it run always, run: sudo loginctl enable-linger {user}");

        /// <summary>
        /// Where systemctl --user and journalctl --user find the user manager, when the environment does
        /// not say so already, or null. A shell from su or sudo -u has no XDG_RUNTIME_DIR, or the calling
        /// user's; a login or lingering makes /run/user/&lt;uid&gt;, and with it the manager's socket.
        /// </summary>
        internal static string? UserRuntimeDirectory(string? current, uint userId, Func<string, bool> directoryExists)
        {
            string own = $"/run/user/{userId}";
            return current != own && directoryExists(own) ? own : null;
        }

        private static string? FirstLine(string text) =>
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

        /// <summary>Whether <c>loginctl show-user &lt;user&gt; -p Linger</c> printed <c>Linger=yes</c>.</summary>
        internal static bool LingerIsOn(string output) =>
            output.Split('\n').Any(line => line.Trim() == "Linger=yes");

        /// <summary>The service's last 100 journal lines, the same as <c>tail</c> shows on macOS.</summary>
        internal static string[] JournalctlArguments(UpdaemonScope scope, string serviceName, bool follow)
        {
            return [.. scope.SystemdScopeArguments, "--unit", serviceName, "--lines", "100", "--no-pager", .. (follow ? new[] { "--follow" } : Array.Empty<string>())];
        }

        /// <summary>Reads the <c>KEY=value</c> lines of <c>systemctl show</c>.</summary>
        internal static ServiceState ParseState(string output)
        {
            Dictionary<string, string> properties = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1].Trim());

            return new ServiceState(
                properties.GetValueOrDefault("ActiveState") == "active",
                int.TryParse(properties.GetValueOrDefault("NRestarts"), out int restarts) ? restarts : 0);
        }

        public async Task<bool> ServiceExistsAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            if (File.Exists(_scope.GetUnitFilePath(serviceName)))
            {
                return true;
            }

            // 'systemctl cat' finds a unit in every directory systemd loads from (/etc, /run, /usr/lib, ...).
            ProcessRunner.Result result = await ProcessRunner.RunAsync("systemctl", Arguments("cat", serviceName + ".service"), cancellationToken);
            return result.Success;
        }

        private async Task SystemctlAsync(CancellationToken cancellationToken, params string[] verb)
        {
            await ProcessRunner.RunAndCheckAsync("systemctl", Arguments(verb), cancellationToken);
        }

        private string[] Arguments(params string[] verb) => [.. _scope.SystemdScopeArguments, .. verb];
    }
}
