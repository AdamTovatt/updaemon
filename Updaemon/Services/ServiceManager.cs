using System.Diagnostics;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Manages systemd services.
    /// </summary>
    public class ServiceManager : IServiceManager
    {
        public async Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await ExecuteSystemctlCommandAsync("start", serviceName, cancellationToken);
        }

        public async Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await ExecuteSystemctlCommandAsync("stop", serviceName, cancellationToken);
        }

        public async Task RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await ExecuteSystemctlCommandAsync("restart", serviceName, cancellationToken);
        }

        public async Task EnableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await ExecuteSystemctlCommandAsync("enable", serviceName, cancellationToken);
        }

        public async Task DisableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            await ExecuteSystemctlCommandAsync("disable", serviceName, cancellationToken);
        }

        public async Task<bool> IsServiceRunningAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "systemctl",
                    Arguments = $"is-active {serviceName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (Process process = Process.Start(startInfo)!)
                {
                    string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                    await process.WaitForExitAsync(cancellationToken);

                    return output.Trim() == "active";
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ServiceExistsAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            try
            {
                string unitFilePath = $"/etc/systemd/system/{serviceName}.service";
                return await Task.FromResult(File.Exists(unitFilePath));
            }
            catch
            {
                return false;
            }
        }

        private async Task ExecuteSystemctlCommandAsync(string command, string serviceName, CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "systemctl",
                Arguments = $"{command} {serviceName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (Process process = Process.Start(startInfo)!)
            {
                string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                string error = await process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"systemctl {command} {serviceName} failed: {error}");
                }
            }
        }
    }
}

