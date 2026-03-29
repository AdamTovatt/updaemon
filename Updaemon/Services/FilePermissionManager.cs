using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Manages file permissions for executables and directories on Linux systems.
    /// </summary>
    public class FilePermissionManager : IFilePermissionManager
    {
        private readonly IOutputWriter _outputWriter;

        public FilePermissionManager(IOutputWriter outputWriter)
        {
            _outputWriter = outputWriter;
        }

        public Task SetExecutablePermissionsAsync(string executablePath, CancellationToken cancellationToken = default)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo("chmod") { UseShellExecute = false };
                startInfo.ArgumentList.Add("+x");
                startInfo.ArgumentList.Add(executablePath);
                System.Diagnostics.Process? process = System.Diagnostics.Process.Start(startInfo);
                process?.WaitForExit();
                _outputWriter.WriteLine($"Set executable permissions on: {executablePath}");
            }
            catch
            {
                _outputWriter.WriteLine($"Warning: Could not set executable permissions on {executablePath}. You may need to run 'chmod +x' manually.");
            }

            return Task.CompletedTask;
        }

        public Task SetDirectoryPermissionsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo("chmod") { UseShellExecute = false };
                startInfo.ArgumentList.Add("-R");
                startInfo.ArgumentList.Add("a+rX");
                startInfo.ArgumentList.Add(directoryPath);
                System.Diagnostics.Process? process = System.Diagnostics.Process.Start(startInfo);
                process?.WaitForExit();
                _outputWriter.WriteLine($"Set directory permissions on: {directoryPath}");
            }
            catch
            {
                _outputWriter.WriteLine($"Warning: Could not set directory permissions on {directoryPath}. You may need to run 'chmod -R a+rX' manually.");
            }

            return Task.CompletedTask;
        }
    }
}

