using System.Diagnostics;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Manages systemd services.
    /// </summary>
    public class ServiceManager : IServiceManager
    {
        public async Task StartServiceAsync(string serviceName)
        {
            await ExecuteSystemctlCommandAsync("start", serviceName);
        }

        public async Task StopServiceAsync(string serviceName)
        {
            await ExecuteSystemctlCommandAsync("stop", serviceName);
        }

        public async Task RestartServiceAsync(string serviceName)
        {
            await ExecuteSystemctlCommandAsync("restart", serviceName);
        }

        public async Task EnableServiceAsync(string serviceName)
        {
            await ExecuteSystemctlCommandAsync("enable", serviceName);
        }

        public async Task DisableServiceAsync(string serviceName)
        {
            await ExecuteSystemctlCommandAsync("disable", serviceName);
        }

        public async Task<bool> IsServiceRunningAsync(string serviceName)
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
                    string output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    return output.Trim() == "active";
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ServiceExistsAsync(string serviceName)
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

        private async Task ExecuteSystemctlCommandAsync(string command, string serviceName)
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
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"systemctl {command} {serviceName} failed: {error}");
                }
            }
        }
    }
}

