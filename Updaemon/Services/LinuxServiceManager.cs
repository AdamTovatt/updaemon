using Updaemon.Configuration;
using Updaemon.Interfaces;

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

        public async Task<bool> IsServiceRunningAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            try
            {
                ProcessRunner.Result result = await ProcessRunner.RunAsync("systemctl", Arguments("is-active", serviceName), cancellationToken);
                return result.StdOut.Trim() == "active";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return false;
            }
        }

        public Task DaemonReloadAsync(CancellationToken cancellationToken = default)
            => SystemctlAsync(cancellationToken, "daemon-reload");

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
