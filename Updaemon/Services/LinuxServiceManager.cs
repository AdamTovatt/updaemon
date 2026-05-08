using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Linux implementation of <see cref="IServiceManager"/> backed by systemd / systemctl.
    /// Constructed only on Linux by Program.cs.
    /// </summary>
    public class LinuxServiceManager : IServiceManager
    {
        public async Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await ProcessRunner.RunAndCheckAsync("systemctl", new[] { "start", serviceName }, cancellationToken);
        }

        public async Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await ProcessRunner.RunAndCheckAsync("systemctl", new[] { "stop", serviceName }, cancellationToken);
        }

        public async Task RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await ProcessRunner.RunAndCheckAsync("systemctl", new[] { "restart", serviceName }, cancellationToken);
        }

        public async Task EnableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await ProcessRunner.RunAndCheckAsync("systemctl", new[] { "enable", serviceName }, cancellationToken);
        }

        public async Task DisableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await ProcessRunner.RunAndCheckAsync("systemctl", new[] { "disable", serviceName }, cancellationToken);
        }

        public async Task<bool> IsServiceRunningAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            try
            {
                ProcessRunner.Result result = await ProcessRunner.RunAsync("systemctl", new[] { "is-active", serviceName }, cancellationToken);
                return result.StdOut.Trim() == "active";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return false;
            }
        }

        public async Task DaemonReloadAsync(CancellationToken cancellationToken = default)
        {
            await ProcessRunner.RunAndCheckAsync("systemctl", new[] { "daemon-reload" }, cancellationToken);
        }

        public Task<bool> ServiceExistsAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            string unitFilePath = $"/etc/systemd/system/{serviceName}.service";
            return Task.FromResult(File.Exists(unitFilePath));
        }
    }
}
