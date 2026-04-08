using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Handles the shared deploy pipeline: download, find executable, set permissions, create symlink.
    /// </summary>
    public class ServiceDeployer : IServiceDeployer
    {
        private readonly ISymlinkManager _symlinkManager;
        private readonly IExecutableDetector _executableDetector;
        private readonly IFilePermissionManager _filePermissionManager;
        private readonly IOutputWriter _outputWriter;
        private readonly string _serviceBaseDirectory;

        public ServiceDeployer(
            ISymlinkManager symlinkManager,
            IExecutableDetector executableDetector,
            IFilePermissionManager filePermissionManager,
            IOutputWriter outputWriter)
        {
            _symlinkManager = symlinkManager;
            _executableDetector = executableDetector;
            _filePermissionManager = filePermissionManager;
            _outputWriter = outputWriter;
            _serviceBaseDirectory = "/opt";
        }

        public ServiceDeployer(
            ISymlinkManager symlinkManager,
            IExecutableDetector executableDetector,
            IFilePermissionManager filePermissionManager,
            IOutputWriter outputWriter,
            string serviceBaseDirectory)
        {
            _symlinkManager = symlinkManager;
            _executableDetector = executableDetector;
            _filePermissionManager = filePermissionManager;
            _outputWriter = outputWriter;
            _serviceBaseDirectory = serviceBaseDirectory;
        }

        public string GetSymlinkPath(string localName)
        {
            return Path.Combine(_serviceBaseDirectory, localName, "current");
        }

        public async Task<string?> ReadCurrentTargetAsync(string localName, CancellationToken cancellationToken = default)
        {
            string symlinkPath = GetSymlinkPath(localName);
            return await _symlinkManager.ReadSymlinkAsync(symlinkPath, cancellationToken);
        }

        public async Task<DeployResult?> DeployVersionAsync(
            RegisteredService service,
            Version version,
            IDistributionServiceClient distributionClient,
            CancellationToken cancellationToken = default)
        {
            // Download
            string versionDirectory = Path.Combine(_serviceBaseDirectory, service.LocalName, version.ToString());
            _outputWriter.WriteLine($"Downloading to: {versionDirectory}");

            Directory.CreateDirectory(versionDirectory);
            await distributionClient.DownloadVersionAsync(service.RemoteName, version, versionDirectory, cancellationToken);
            _outputWriter.WriteLine("Download complete");

            // Find executable
            string executableName = service.ExecutableName ?? service.LocalName;
            string? executablePath = await _executableDetector.FindExecutableAsync(versionDirectory, executableName, cancellationToken);
            if (executablePath == null)
            {
                _outputWriter.WriteError($"Error: Could not find executable in {versionDirectory} with name {executableName}");
                return null;
            }

            _outputWriter.WriteLine($"Found executable: {executablePath}");

            // Set file permissions
            await _filePermissionManager.SetExecutablePermissionsAsync(executablePath, cancellationToken);
            string serviceDirectory = Path.Combine(_serviceBaseDirectory, service.LocalName);
            await _filePermissionManager.SetDirectoryPermissionsAsync(serviceDirectory, cancellationToken);

            // Create/update symlink
            string symlinkPath = GetSymlinkPath(service.LocalName);
            await _symlinkManager.CreateOrUpdateSymlinkAsync(symlinkPath, versionDirectory, cancellationToken);
            _outputWriter.WriteLine($"Updated symlink: {symlinkPath} -> {versionDirectory}");

            return new DeployResult
            {
                VersionDirectory = versionDirectory,
                ExecutablePath = executablePath,
                SymlinkPath = symlinkPath,
            };
        }

        public async Task CleanupDeployAsync(DeployResult result, CancellationToken cancellationToken = default)
        {
            // Remove symlink so the service does not appear initialized
            try
            {
                bool isSymlink = await _symlinkManager.IsSymlinkAsync(result.SymlinkPath, cancellationToken);
                if (isSymlink)
                {
                    string? target = await _symlinkManager.ReadSymlinkAsync(result.SymlinkPath, cancellationToken);
                    if (target == result.VersionDirectory)
                    {
                        File.Delete(result.SymlinkPath);
                    }
                }
            }
            catch
            {
                // Best effort
            }

            // Remove downloaded version directory
            try
            {
                if (Directory.Exists(result.VersionDirectory))
                {
                    Directory.Delete(result.VersionDirectory, recursive: true);
                }
            }
            catch
            {
                // Best effort
            }
        }
    }
}
