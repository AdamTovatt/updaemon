using Updaemon.Common.Models;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.IO;
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
        private readonly TimeProvider _time;

        private static readonly TimeSpan StartPollInterval = TimeSpan.FromMilliseconds(250);

        public ServiceDeployer(
            UpdaemonScope scope,
            ISymlinkManager symlinkManager,
            IExecutableDetector executableDetector,
            IFilePermissionManager filePermissionManager,
            AppFileWriter appFileWriter,
            IServiceManager serviceManager,
            ISecretsManager secretsManager,
            IOutputWriter outputWriter,
            TimeProvider time)
        {
            _scope = scope;
            _symlinkManager = symlinkManager;
            _executableDetector = executableDetector;
            _filePermissionManager = filePermissionManager;
            _appFileWriter = appFileWriter;
            _serviceManager = serviceManager;
            _secretsManager = secretsManager;
            _outputWriter = outputWriter;
            _time = time;
        }

        public async Task<Version> GetCurrentVersionAsync(string appName, CancellationToken cancellationToken = default)
        {
            return await TryGetCurrentVersionAsync(appName, cancellationToken) ?? throw NotDeployed(appName);
        }

        public async Task<AppManifest> GetCurrentManifestAsync(string appName, CancellationToken cancellationToken = default)
        {
            return await TryReadCurrentManifestAsync(appName, cancellationToken) ?? throw NotDeployed(appName);
        }

        public async Task<DeployableVersion> StageAsync(InstalledApp app, Version version, IDistributionServiceClient distributionClient, CancellationToken cancellationToken = default)
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
                DownloadResult download = await distributionClient.DownloadVersionAsync(app.Remote, version, versionDirectory, cancellationToken);
                if (!download.Verified)
                {
                    _outputWriter.WriteLine($"Not verified: {app.Name} {version} came without a checksum to check it against.");
                }

                AppManifest? releaseManifest = await AppManifest.ReadAsync(versionDirectory, cancellationToken);
                if (releaseManifest != null)
                {
                    return new DeployableVersion { Version = version, Directory = versionDirectory, Manifest = releaseManifest, ManifestInDirectory = true };
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
                return new DeployableVersion { Version = version, Directory = versionDirectory, Manifest = manifest, ManifestInDirectory = false };
            }
            catch
            {
                DeleteStaged(versionDirectory);
                throw;
            }
        }

        public async Task<ReachableServiceManager> ReachServiceManagerAsync(string appName, AppManifest? next = null, CancellationToken cancellationToken = default)
        {
            // An app without a service, now or next, never touches the service manager: nothing to check.
            return next?.IsService == true || HasService(appName)
                ? await _serviceManager.EnsureReachableAsync(cancellationToken)
                : new ReachableServiceManager();
        }

        public bool HasService(string appName) => File.Exists(_scope.GetUnitFilePath(appName));

        public void Discard(DeployableVersion staged)
        {
            // An activation whose switch back failed leaves it current: it is then all the app has.
            string appDirectory = Path.GetDirectoryName(staged.Directory)!;
            string currentLink = _scope.GetCurrentLinkPath(Path.GetFileName(appDirectory));
            if (new FileInfo(currentLink).LinkTarget is string target
                && Path.GetFullPath(target, appDirectory) == Path.GetFullPath(staged.Directory))
            {
                return;
            }

            DeleteStaged(staged.Directory);
        }

        /// <summary>Deletes a version directory that was never activated, and the app's directory if that leaves it empty.</summary>
        private static void DeleteStaged(string versionDirectory)
        {
            TryDeleteDirectory(versionDirectory);

            // A first install's version was all there was: leave no empty directory behind.
            string appDirectory = Path.GetDirectoryName(versionDirectory)!;
            try
            {
                if (Directory.Exists(appDirectory) && !Directory.EnumerateFileSystemEntries(appDirectory).Any())
                {
                    Directory.Delete(appDirectory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort, as for the version itself.
            }
        }

        public IReadOnlyList<Version> GetRetainedVersions(string appName)
        {
            return GetVersionDirectories(appName).Select(entry => entry.Version).OrderBy(version => version).ToList();
        }

        public async Task<DeployableVersion> OpenRetainedAsync(string appName, Version version, CancellationToken cancellationToken = default)
        {
            string directory = Path.Combine(_scope.GetAppDirectory(appName), version.ToString());
            AppManifest manifest = (Directory.Exists(directory) ? await AppManifest.ReadAsync(directory, cancellationToken) : null)
                ?? throw new InvalidOperationException(
                    $"{appName} {version} is not kept. Kept versions: {string.Join(", ", GetRetainedVersions(appName))}.");

            return new DeployableVersion { Version = version,Directory = directory, Manifest = manifest, ManifestInDirectory = true };
        }

        public async Task ActivateAsync(InstalledApp app, DeployableVersion version, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            AppManifest manifest = version.Manifest;
            if (manifest.Command != null)
            {
                _appFileWriter.EnsureCommandIsFree(manifest.Command, app.Name);
            }

            string executablePath = Path.Combine(version.Directory, manifest.Executable);
            if (!File.Exists(executablePath))
            {
                throw new InvalidOperationException($"{app.Name} {version.Version}: the executable '{manifest.Executable}' named in {AppManifest.FileName} is not in the release.");
            }

            if (!version.ManifestInDirectory)
            {
                // Every version directory carries the manifest it runs with: the current one is read
                // back by 'set', 'unset' and the next update.
                await manifest.WriteAsync(version.Directory, cancellationToken);
            }

            await _filePermissionManager.SetExecutablePermissionsAsync(executablePath, cancellationToken);
            await _filePermissionManager.SetDirectoryPermissionsAsync(_scope.GetAppDirectory(app.Name), cancellationToken);

            string currentLink = _scope.GetCurrentLinkPath(app.Name);
            string? previousTarget = await _symlinkManager.ReadSymlinkAsync(currentLink, cancellationToken);
            AppManifest? previous = await TryReadCurrentManifestAsync(app.Name, cancellationToken);
            await _symlinkManager.CreateOrUpdateSymlinkAsync(currentLink, version.Directory, cancellationToken);
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

        public async Task<bool> ApplySettingsAsync(InstalledApp app, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            AppManifest manifest = await GetCurrentManifestAsync(app.Name, cancellationToken);
            await _appFileWriter.WriteAsync(app, manifest, manifest, cancellationToken);

            if (!manifest.IsService || !await RestartIfRunningAsync(app.Name, cancellationToken))
            {
                return true;
            }

            _outputWriter.WriteLine($"Restarted {app.Name}.");
            return await StaysRunningAsync(app.Name, cancellationToken);
        }

        public async Task<bool> StartAsync(string appName, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            await _serviceManager.EnableServiceAsync(appName, cancellationToken);
            await _serviceManager.StartServiceAsync(appName, cancellationToken);
            return await StaysRunningAsync(appName, cancellationToken);
        }

        /// <summary>
        /// What a false <see cref="StartAsync"/> or <see cref="RestartAsync"/> means:
        /// <c>{version} did not stay running{consequence}.</c>, then where to look. The caller names the app in front.
        /// </summary>
        public static string NotRunning(string appName, Version version, string consequence = "") =>
            $"{version} did not stay running{consequence}. See 'updaemon logs {appName}'.";

        public async Task<bool> RestartAsync(string appName, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            if (await IsRunningAsync(appName, cancellationToken))
            {
                await _serviceManager.RestartServiceAsync(appName, cancellationToken);
            }
            else
            {
                await _serviceManager.StartServiceAsync(appName, cancellationToken);
            }

            return await StaysRunningAsync(appName, cancellationToken);
        }

        /// <summary>Restarts the service if it is running. Returns whether it did.</summary>
        private async Task<bool> RestartIfRunningAsync(string appName, CancellationToken cancellationToken)
        {
            if (!await IsRunningAsync(appName, cancellationToken))
            {
                return false;
            }

            await _serviceManager.RestartServiceAsync(appName, cancellationToken);
            return true;
        }

        public async Task RemoveAsync(InstalledApp app, bool purge, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
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
                if (_scope.Platform.IsMacOS)
                {
                    // launchd's log files; on Linux the journal keeps its own and is left alone.
                    Files.DeleteIfExists(_scope.GetOutputLogPath(app.Name));
                    Files.DeleteIfExists(_scope.GetErrorLogPath(app.Name));
                }
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

            List<string> directoriesToDelete = GetVersionDirectories(appName)
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

        private async Task<bool> IsRunningAsync(string appName, CancellationToken cancellationToken)
        {
            return (await _serviceManager.GetStateAsync(appName, cancellationToken)).IsActive;
        }

        /// <summary>
        /// Right after a (re)start: whether the service runs, then still runs after the current version's
        /// health-check window without the service manager having started it again. launchd starts a
        /// loaded job a moment after <c>bootstrap</c> returns, so the first reading waits, within the same
        /// window, for it to come up.
        /// </summary>
        private async Task<bool> StaysRunningAsync(string appName, CancellationToken cancellationToken)
        {
            AppManifest manifest = await GetCurrentManifestAsync(appName, cancellationToken);
            // Only a service is started, so only a service is checked.
            TimeSpan window = (manifest.Service ?? throw new InvalidOperationException($"{appName}'s current version is not a service.")).HealthCheck;

            DateTimeOffset deadline = _time.GetUtcNow() + window;
            ServiceState before = await _serviceManager.GetStateAsync(appName, cancellationToken);
            while (!before.IsActive && _time.GetUtcNow() < deadline)
            {
                await Task.Delay(StartPollInterval, _time, cancellationToken);
                before = await _serviceManager.GetStateAsync(appName, cancellationToken);
            }

            if (!before.IsActive)
            {
                return false;
            }

            await Task.Delay(window, _time, cancellationToken);
            ServiceState after = await _serviceManager.GetStateAsync(appName, cancellationToken);
            return after.IsActive && after.Starts == before.Starts;
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

        /// <summary>The app's version directories (named by version) with their full paths.</summary>
        private IEnumerable<(string Path, Version Version)> GetVersionDirectories(string appName)
        {
            string appDirectory = _scope.GetAppDirectory(appName);
            if (!Directory.Exists(appDirectory))
            {
                yield break;
            }

            foreach (string directory in Directory.GetDirectories(appDirectory))
            {
                if (Version.TryParse(Path.GetFileName(directory), out Version? version))
                {
                    yield return (Path.GetFullPath(directory), version);
                }
            }
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
