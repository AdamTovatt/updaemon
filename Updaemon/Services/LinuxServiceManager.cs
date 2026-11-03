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

        public Task RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
            => SystemctlAsync(cancellationToken, "restart", serviceName);

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
