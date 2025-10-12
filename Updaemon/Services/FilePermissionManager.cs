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
                System.Diagnostics.Process? process = System.Diagnostics.Process.Start("chmod", $"+x {executablePath}");
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
                System.Diagnostics.Process? process = System.Diagnostics.Process.Start("chmod", $"-R a+rX {directoryPath}");
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

