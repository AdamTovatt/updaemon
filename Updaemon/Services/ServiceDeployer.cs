using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.IO;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// The deploy pipeline for an app: have <see cref="VersionStore"/> download a version and point the app's
    /// <c>current</c> link at it, resolve its manifest and have <see cref="AppFileWriter"/> generate its files.
    /// Also starts, restarts and removes apps. <see cref="AppSwitcher"/> puts back what fails.
    /// </summary>
    public class ServiceDeployer : IServiceDeployer
    {
        private readonly UpdaemonScope _scope;
        private readonly VersionStore _versions;
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
            VersionStore versions,
            IExecutableDetector executableDetector,
            IFilePermissionManager filePermissionManager,
            AppFileWriter appFileWriter,
            IServiceManager serviceManager,
            ISecretsManager secretsManager,
            IOutputWriter outputWriter,
            TimeProvider time)
        {
            _scope = scope;
            _versions = versions;
            _executableDetector = executableDetector;
            _filePermissionManager = filePermissionManager;
            _appFileWriter = appFileWriter;
            _serviceManager = serviceManager;
            _secretsManager = secretsManager;
            _outputWriter = outputWriter;
            _time = time;
        }

        public async Task<AppManifest> GetCurrentManifestAsync(string appName, CancellationToken cancellationToken = default)
        {
            return await TryReadCurrentManifestAsync(appName, cancellationToken) ?? throw VersionStore.NotDeployed(new AppName(appName));
        }

        public async Task<DeployableVersion> StageAsync(InstalledApp app, Version version, IDistributionServiceClient distributionClient, CancellationToken cancellationToken = default)
        {
            (AppManifest manifest, bool inDirectory) = await _versions.DownloadAsync(
                app.PackageName,
                app,
                version,
                distributionClient,
                async directory =>
                {
                    AppManifest? releaseManifest = await AppManifest.ReadAsync(directory, cancellationToken);
                    AppManifest resolved = releaseManifest ?? await FindManifestAsync(app, version, directory, cancellationToken);
                    await MakeExecutableAsync(app.Name, version, resolved, directory, cancellationToken);
                    return (resolved, releaseManifest != null);
                },
                cancellationToken);
            return new DeployableVersion { Version = version, Directory = _versions.GetVersionDirectory(app.PackageName, version), Manifest = manifest, ManifestInDirectory = inDirectory };
        }

        /// <summary>The manifest of a release that ships none: the app's from install flags.</summary>
        private async Task<AppManifest> FindManifestAsync(InstalledApp app, Version version, string directory, CancellationToken cancellationToken)
        {
            if (app.Manifest == null)
            {
                throw new MissingManifestException($"{app.Name} {version} has no {AppManifest.FileName}.");
            }

            // A manifest from install flags names the executable by file name only; find it in the release.
            string executableName = Path.GetFileName(app.Manifest.Executable);
            string? executablePath = await _executableDetector.FindExecutableAsync(directory, executableName, cancellationToken);
            if (executablePath == null)
            {
                throw new InvalidOperationException($"Could not find an executable named '{executableName}' in {app.Name} {version}.");
            }

            return app.Manifest with { Executable = Path.GetRelativePath(directory, executablePath) };
        }

        public Task MakeExecutableAsync(string appName, DeployableVersion version, CancellationToken cancellationToken = default) =>
            MakeExecutableAsync(appName, version.Version, version.Manifest, version.Directory, cancellationToken);

        private async Task MakeExecutableAsync(string appName, Version version, AppManifest manifest, string directory, CancellationToken cancellationToken)
        {
            string path = Path.Combine(directory, manifest.Executable);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"{appName} {version}: the executable '{manifest.Executable}' named in {AppManifest.FileName} is not in the release.");
            }

            await _filePermissionManager.SetExecutablePermissionsAsync(path, cancellationToken);
        }

        public async Task<ReachableServiceManager> ReachServiceManagerAsync(string appName, AppManifest? next = null, CancellationToken cancellationToken = default)
        {
            // An app without a service, now or next, never touches the service manager: nothing to check.
            return next?.IsService == true || HasService(appName)
                ? await _serviceManager.EnsureReachableAsync(cancellationToken)
                : new ReachableServiceManager();
        }

        public bool HasService(string appName) => File.Exists(_scope.GetUnitFilePath(appName));

        public async Task<DeployableVersion> OpenRetainedAsync(string appName, Version version, CancellationToken cancellationToken = default)
        {
            AppName name = new AppName(appName);
            string directory = _versions.OpenRetained(name, version);
            AppManifest manifest = await AppManifest.ReadAsync(directory, cancellationToken) ?? throw _versions.NotKept(name, version);

            return new DeployableVersion { Version = version, Directory = directory, Manifest = manifest, ManifestInDirectory = true };
        }

        public async Task<DeployableVersion> OpenCurrentAsync(string appName, CancellationToken cancellationToken = default)
        {
            return await OpenRetainedAsync(appName, await _versions.GetCurrentAsync(new AppName(appName), cancellationToken), cancellationToken);
        }

        public async Task ActivateAsync(InstalledApp app, DeployableVersion version, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            AppManifest manifest = version.Manifest;
            if (manifest.Command != null)
            {
                _appFileWriter.EnsureCommandIsFree(manifest.Command, app.Name);
            }

            if (!version.ManifestInDirectory)
            {
                // Every version directory carries the manifest it runs with: the current one is read
                // back by 'set', 'unset' and the next update.
                await manifest.WriteAsync(version.Directory, cancellationToken);
            }

            AppManifest? previous = await TryReadCurrentManifestAsync(app.Name, cancellationToken);
            await _versions.PointAsync(app.PackageName, version.Version, cancellationToken);
            await _appFileWriter.WriteAsync(app, manifest, previous, cancellationToken);
        }

        public async Task RegenerateAsync(PreparedApp prepared, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            InstalledApp app = prepared.App;
            AppManifest manifest = await GetCurrentManifestAsync(app.Name, cancellationToken);
            await _appFileWriter.WriteAsync(app, manifest, manifest, cancellationToken);
        }

        public async Task<bool> ApplySettingsAsync(PreparedApp prepared, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            prepared.BeginChange();
            InstalledApp app = prepared.App;
            await RegenerateAsync(prepared, reachable, cancellationToken);
            if (!HasService(app.Name) || !await _serviceManager.IsEnabledAsync(app.Name, cancellationToken))
            {
                return true;
            }

            // Running or not: one crash-looping between automatic restarts would otherwise keep its old
            // unit until the next update.
            await _serviceManager.RestartServiceAsync(app.Name, cancellationToken);
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
            $"{version} did not stay running{consequence}. {SeeLogs(appName)}";

        /// <summary>What <see cref="NotRunning"/> says where no version needs naming: the start of a sentence, without its period.</summary>
        public static string NotRunningAfterRestart(string appName) => $"{appName} did not stay running after the restart";

        /// <summary>Where a service that did not stay running says why.</summary>
        public static string SeeLogs(string appName) => $"See 'updaemon logs {appName}'.";

        public async Task<bool> RestartAsync(string appName, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            await _serviceManager.RestartServiceAsync(appName, cancellationToken);
            return await StaysRunningAsync(appName, cancellationToken);
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
            Files.DeleteDirectoryIfExists(_scope.GetAppDirectory(app.Name));
            _secretsManager.DeleteAppSecrets(app.Name);
            if (purge)
            {
                Files.DeleteDirectoryIfExists(_scope.GetDataDirectory(app.Name));
                if (_scope.Platform.IsMacOS)
                {
                    // launchd's log files; on Linux the journal keeps its own and is left alone.
                    Files.DeleteIfExists(_scope.GetOutputLogPath(app.Name));
                    Files.DeleteIfExists(_scope.GetErrorLogPath(app.Name));
                }
            }
        }

        /// <remarks>
        /// Runs it, then still runs after the window? launchd starts a loaded job a moment after
        /// <c>bootstrap</c> returns, so the first reading waits, within the same window, for it to come up.
        /// </remarks>
        public async Task<bool> StaysRunningAsync(string appName, CancellationToken cancellationToken = default)
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

        private async Task<AppManifest?> TryReadCurrentManifestAsync(string appName, CancellationToken cancellationToken)
        {
            return await _versions.FindCurrentAsync(new AppName(appName), cancellationToken) == null
                ? null
                : await AppManifest.ReadAsync(_scope.GetCurrentLinkPath(appName), cancellationToken);
        }
    }
}
