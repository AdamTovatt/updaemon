using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// The deploy pipeline for an app: download a version, resolve its manifest, point the app's
    /// <c>current</c> link at it and have <see cref="AppFileWriter"/> generate its files. Also
    /// starts, restarts and removes apps.
    /// </summary>
    public class ServiceDeployer : IServiceDeployer
    {
        private readonly UpdaemonScope _scope;
        private readonly ISymlinkManager _symlinkManager;
        private readonly IExecutableDetector _executableDetector;
        private readonly IFilePermissionManager _filePermissionManager;
        private readonly AppFileWriter _appFileWriter;
        private readonly IServiceManager _serviceManager;
        private readonly ISecretsManager _secretsManager;
        private readonly IOutputWriter _outputWriter;

        public ServiceDeployer(
            UpdaemonScope scope,
            ISymlinkManager symlinkManager,
            IExecutableDetector executableDetector,
            IFilePermissionManager filePermissionManager,
            AppFileWriter appFileWriter,
            IServiceManager serviceManager,
            ISecretsManager secretsManager,
            IOutputWriter outputWriter)
        {
            _scope = scope;
            _symlinkManager = symlinkManager;
            _executableDetector = executableDetector;
            _filePermissionManager = filePermissionManager;
            _appFileWriter = appFileWriter;
            _serviceManager = serviceManager;
            _secretsManager = secretsManager;
            _outputWriter = outputWriter;
        }

        public async Task<Version> GetCurrentVersionAsync(string appName, CancellationToken cancellationToken = default)
        {
            return await TryGetCurrentVersionAsync(appName, cancellationToken) ?? throw NotDeployed(appName);
        }

        public async Task<AppManifest> GetCurrentManifestAsync(string appName, CancellationToken cancellationToken = default)
        {
            return await TryReadCurrentManifestAsync(appName, cancellationToken) ?? throw NotDeployed(appName);
        }

        public async Task<StagedVersion> StageAsync(InstalledApp app, Version version, IDistributionServiceClient distributionClient, CancellationToken cancellationToken = default)
        {
            string versionDirectory = Path.Combine(_scope.GetAppDirectory(app.Name), version.ToString());
            if (Directory.Exists(versionDirectory))
            {
                if (version == await TryGetCurrentVersionAsync(app.Name, cancellationToken))
                {
                    throw new InvalidOperationException($"{app.Name} {version} is already the current version.");
                }

                // Left over from an attempt that was not activated.
                Directory.Delete(versionDirectory, recursive: true);
            }

            try
            {
                _outputWriter.WriteLine($"Downloading {app.Name} {version}...");
                Directory.CreateDirectory(versionDirectory);
                await distributionClient.DownloadVersionAsync(app.Remote, version, versionDirectory, cancellationToken);

                AppManifest? releaseManifest = await AppManifest.ReadAsync(versionDirectory, cancellationToken);
                if (releaseManifest != null)
                {
                    return new StagedVersion { Version = version, Directory = versionDirectory, Manifest = releaseManifest, ManifestFromRelease = true };
                }

                if (app.Manifest == null)
                {
                    throw new MissingManifestException($"{app.Name} {version} has no {AppManifest.FileName}.");
                }

                // A manifest from install flags names the executable by file name only; find it in the release.
                string executableName = Path.GetFileName(app.Manifest.Executable);
                string? executablePath = await _executableDetector.FindExecutableAsync(versionDirectory, executableName, cancellationToken);
                if (executablePath == null)
                {
                    throw new InvalidOperationException($"Could not find an executable named '{executableName}' in {app.Name} {version}.");
                }

                AppManifest manifest = app.Manifest with { Executable = Path.GetRelativePath(versionDirectory, executablePath) };
                return new StagedVersion { Version = version, Directory = versionDirectory, Manifest = manifest, ManifestFromRelease = false };
            }
            catch
            {
                TryDeleteDirectory(versionDirectory);
                throw;
            }
        }

        public void Discard(StagedVersion staged)
        {
            TryDeleteDirectory(staged.Directory);
        }

        public async Task ActivateAsync(InstalledApp app, StagedVersion staged, CancellationToken cancellationToken = default)
        {
            AppManifest manifest = staged.Manifest;
            if (manifest.Command != null)
            {
                _appFileWriter.EnsureCommandIsFree(manifest.Command, app.Name);
            }

            string executablePath = Path.Combine(staged.Directory, manifest.Executable);
            if (!File.Exists(executablePath))
            {
                throw new InvalidOperationException($"{app.Name} {staged.Version}: the executable '{manifest.Executable}' named in {AppManifest.FileName} is not in the release.");
            }

            if (!staged.ManifestFromRelease)
            {
                // Every version directory carries the manifest it runs with: the current one is read
                // back by 'set', 'unset' and the next update.
                await manifest.WriteAsync(staged.Directory, cancellationToken);
            }

            await _filePermissionManager.SetExecutablePermissionsAsync(executablePath, cancellationToken);
            await _filePermissionManager.SetDirectoryPermissionsAsync(_scope.GetAppDirectory(app.Name), cancellationToken);

            string currentLink = _scope.GetCurrentLinkPath(app.Name);
            string? previousTarget = await _symlinkManager.ReadSymlinkAsync(currentLink, cancellationToken);
            AppManifest? previous = await TryReadCurrentManifestAsync(app.Name, cancellationToken);
            await _symlinkManager.CreateOrUpdateSymlinkAsync(currentLink, staged.Directory, cancellationToken);
            try
            {
                await _appFileWriter.WriteAsync(app, manifest, previous, cancellationToken);
            }
            catch
            {
                if (previousTarget != null && previous != null)
                {
                    await RevertAsync(app, currentLink, previousTarget, previous, manifest);
                }

                throw;
            }
        }

        public async Task ApplySettingsAsync(InstalledApp app, CancellationToken cancellationToken = default)
        {
            AppManifest manifest = await GetCurrentManifestAsync(app.Name, cancellationToken);
            await _appFileWriter.WriteAsync(app, manifest, manifest, cancellationToken);

            if (manifest.IsService && await RestartIfRunningAsync(app.Name, cancellationToken))
            {
                _outputWriter.WriteLine($"Restarted {app.Name}.");
            }
        }

        public async Task StartAsync(string appName, CancellationToken cancellationToken = default)
        {
            await _serviceManager.EnableServiceAsync(appName, cancellationToken);
            await _serviceManager.StartServiceAsync(appName, cancellationToken);
        }

        public async Task RestartAsync(string appName, CancellationToken cancellationToken = default)
        {
            if (await _serviceManager.IsServiceRunningAsync(appName, cancellationToken))
            {
                await _serviceManager.RestartServiceAsync(appName, cancellationToken);
            }
            else
            {
                await _serviceManager.StartServiceAsync(appName, cancellationToken);
            }
        }

        public async Task<bool> RestartIfRunningAsync(string appName, CancellationToken cancellationToken = default)
        {
            if (!await _serviceManager.IsServiceRunningAsync(appName, cancellationToken))
            {
                return false;
            }

            await _serviceManager.RestartServiceAsync(appName, cancellationToken);
            return true;
        }

        public async Task RemoveAsync(InstalledApp app, bool purge, CancellationToken cancellationToken = default)
        {
            AppManifest? manifest = null;
            try
            {
                manifest = await TryReadCurrentManifestAsync(app.Name, cancellationToken);
            }
            catch (ManifestException ex)
            {
                _outputWriter.WriteError($"Warning: {ex.Message}");
            }

            await _appFileWriter.RemoveAsync(app.Name, manifest, cancellationToken);
            DeleteDirectoryIfExists(_scope.GetAppDirectory(app.Name));
            _secretsManager.DeleteAppSecrets(app.Name);
            if (purge)
            {
                DeleteDirectoryIfExists(_scope.GetDataDirectory(app.Name));
            }
        }

        public async Task PruneOldVersionsAsync(string appName, int retentionCount, CancellationToken cancellationToken = default)
        {
            string appDirectory = _scope.GetAppDirectory(appName);
            if (!Directory.Exists(appDirectory))
                return;

            if (retentionCount < 1)
                retentionCount = 1;

            string? currentTarget = await _symlinkManager.ReadSymlinkAsync(_scope.GetCurrentLinkPath(appName), cancellationToken);
            string? normalizedCurrentTarget = currentTarget != null ? Path.GetFullPath(currentTarget, appDirectory) : null;

            List<(string Path, Version Version)> versionDirectories = new List<(string, Version)>();
            foreach (string directory in Directory.GetDirectories(appDirectory))
            {
                string dirName = Path.GetFileName(directory);
                if (Version.TryParse(dirName, out Version? version))
                {
                    versionDirectories.Add((Path.GetFullPath(directory), version));
                }
            }

            List<string> directoriesToDelete = versionDirectories
                .OrderByDescending(v => v.Version)
                .Skip(retentionCount)
                .Where(v => v.Path != normalizedCurrentTarget)
                .Select(v => v.Path)
                .ToList();

            foreach (string directory in directoriesToDelete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Directory.Delete(directory, recursive: true);
                    _outputWriter.WriteLine($"Pruned old version: {Path.GetFileName(directory)}");
                }
                catch (Exception ex)
                {
                    _outputWriter.WriteError($"Warning: Failed to prune {Path.GetFileName(directory)}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Points <c>current</c> back at the version that ran before a failed activation and regenerates
        /// its files, so the next update retries instead of finding the failed version current. Runs to
        /// completion even when the activation was cancelled.
        /// </summary>
        private async Task RevertAsync(InstalledApp app, string currentLink, string previousTarget, AppManifest previous, AppManifest failed)
        {
            try
            {
                await _symlinkManager.CreateOrUpdateSymlinkAsync(currentLink, previousTarget, CancellationToken.None);
                await _appFileWriter.WriteAsync(app, previous, failed, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _outputWriter.WriteError($"Warning: could not switch {app.Name} back to its previous version: {ex.Message}");
            }
        }

        private async Task<Version?> TryGetCurrentVersionAsync(string appName, CancellationToken cancellationToken)
        {
            string? target = await _symlinkManager.ReadSymlinkAsync(_scope.GetCurrentLinkPath(appName), cancellationToken);
            return target != null && Version.TryParse(Path.GetFileName(target), out Version? version) ? version : null;
        }

        private async Task<AppManifest?> TryReadCurrentManifestAsync(string appName, CancellationToken cancellationToken)
        {
            string currentLink = _scope.GetCurrentLinkPath(appName);
            if (await _symlinkManager.ReadSymlinkAsync(currentLink, cancellationToken) == null)
            {
                return null;
            }

            return await AppManifest.ReadAsync(currentLink, cancellationToken);
        }

        private static InvalidOperationException NotDeployed(string appName) =>
            new InvalidOperationException($"{appName} has no current version. Remove it with 'updaemon remove {appName}', then install it again.");

        private static void DeleteDirectoryIfExists(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        /// <summary>For cleaning up a version that was never activated: a leftover is replaced on the next attempt.</summary>
        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                DeleteDirectoryIfExists(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort, see above.
            }
        }
    }
}
