using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.IO;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Writes and removes what updaemon generates for an app from its manifest and settings: the
    /// data directory, the settings file, the command wrapper on PATH and the service unit.
    /// </summary>
    public class AppFileWriter
    {
        private readonly UpdaemonScope _scope;
        private readonly IFilePermissionManager _filePermissionManager;
        private readonly IUnitFileManager _unitFileManager;
        private readonly IServiceManager _serviceManager;
        private readonly ISecretsManager _secretsManager;
        private readonly IOutputWriter _outputWriter;

        public AppFileWriter(
            UpdaemonScope scope,
            IFilePermissionManager filePermissionManager,
            IUnitFileManager unitFileManager,
            IServiceManager serviceManager,
            ISecretsManager secretsManager,
            IOutputWriter outputWriter)
        {
            _scope = scope;
            _filePermissionManager = filePermissionManager;
            _unitFileManager = unitFileManager;
            _serviceManager = serviceManager;
            _secretsManager = secretsManager;
            _outputWriter = outputWriter;
        }

        /// <summary>
        /// Throws unless the command's path on PATH is free or holds this app's own wrapper.
        /// </summary>
        public void EnsureCommandIsFree(string command, string appName)
        {
            string path = _scope.GetCommandPath(command);
            bool exists = File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget != null;
            if (!exists)
            {
                return;
            }

            string? owner = CommandWrapper.ReadOwner(path);
            if (owner == null)
            {
                throw new InvalidOperationException($"'{path}' already exists and updaemon did not create it. Remove it, then try again.");
            }

            if (owner != appName)
            {
                throw new InvalidOperationException($"The command '{command}' already belongs to the app '{owner}'.");
            }
        }

        /// <summary>
        /// Generates the app's files for <paramref name="manifest"/>, removing what
        /// <paramref name="previous"/> had and <paramref name="manifest"/> no longer has (a renamed
        /// command, a service that is gone). Does not start or restart the service.
        /// </summary>
        public async Task WriteAsync(InstalledApp app, AppManifest manifest, AppManifest? previous, CancellationToken cancellationToken)
        {
            string dataDirectory = _scope.GetDataDirectory(app.Name);
            Directory.CreateDirectory(dataDirectory);
            if (app.RunAs != null)
            {
                await _filePermissionManager.SetOwnerAsync(dataDirectory, app.RunAs, cancellationToken);
            }

            Dictionary<string, string> settings = new Dictionary<string, string>();
            foreach (KeyValuePair<string, SettingSpec> setting in manifest.Settings)
            {
                if (setting.Value.Default != null)
                {
                    settings[setting.Key] = setting.Value.Default;
                }
            }

            foreach (KeyValuePair<string, string> setting in app.Settings)
            {
                settings[setting.Key] = setting.Value;
            }

            settings[AppManifest.DataDirectoryVariable] = dataDirectory;
            string settingsFilePath = _scope.GetSettingsFilePath(app.Name);
            await EnvFile.WriteAsync(settingsFilePath, settings, AtomicFile.Readable, cancellationToken);

            string currentLink = _scope.GetCurrentLinkPath(app.Name);
            string executablePath = Path.Combine(currentLink, manifest.Executable);
            string secretsFilePath = _scope.GetSecretsFilePath(app.Name);

            if (previous?.Command != null && previous.Command != manifest.Command)
            {
                DeleteOwnWrapper(previous.Command, app.Name);
            }

            if (manifest.Command != null)
            {
                string wrapper = CommandWrapper.Render(app.Name, executablePath, settingsFilePath, secretsFilePath);
                await AtomicFile.WriteAsync(_scope.GetCommandPath(manifest.Command), wrapper, AtomicFile.Executable, cancellationToken);
            }

            if (manifest.Service != null)
            {
                Dictionary<string, string> environment = new Dictionary<string, string>(settings);
                foreach (KeyValuePair<string, string> secret in await _secretsManager.GetAppSecretsAsync(app.Name, cancellationToken))
                {
                    environment[secret.Key] = secret.Value;
                }

                ServiceUnit unit = new ServiceUnit
                {
                    AppName = app.Name,
                    WorkingDirectory = currentLink,
                    ExecutablePath = executablePath,
                    Arguments = app.Args ?? manifest.Service.Args,
                    RunAs = app.RunAs,
                    SettingsFilePath = settingsFilePath,
                    SecretsFilePath = secretsFilePath,
                    Environment = environment,
                };

                await _unitFileManager.WriteUnitFileAsync(unit, cancellationToken);
                await _serviceManager.DaemonReloadAsync(cancellationToken);
            }
            else if (previous?.Service != null)
            {
                await RemoveServiceAsync(app.Name, cancellationToken);
            }
        }

        /// <summary>
        /// Stops and deletes the app's service unit if it has one, deletes its wrapper if it still owns
        /// it, and deletes its settings file. <paramref name="manifest"/> is null when it is unknown.
        /// </summary>
        public async Task RemoveAsync(string appName, AppManifest? manifest, CancellationToken cancellationToken)
        {
            if (File.Exists(_scope.GetUnitFilePath(appName)))
            {
                await RemoveServiceAsync(appName, cancellationToken);
            }

            RemoveCommandAndSettings(appName, manifest);
        }

        /// <summary>
        /// Deletes the app's wrapper if it still owns it, and its settings file; leaves its service
        /// unit. <paramref name="manifest"/> is null when it is unknown.
        /// </summary>
        public void RemoveCommandAndSettings(string appName, AppManifest? manifest)
        {
            if (manifest?.Command != null)
            {
                DeleteOwnWrapper(manifest.Command, appName);
            }

            string settingsFilePath = _scope.GetSettingsFilePath(appName);
            if (File.Exists(settingsFilePath))
            {
                File.Delete(settingsFilePath);
            }
        }

        private void DeleteOwnWrapper(string command, string appName)
        {
            string path = _scope.GetCommandPath(command);
            if (CommandWrapper.ReadOwner(path) == appName)
            {
                File.Delete(path);
            }
        }

        private async Task RemoveServiceAsync(string appName, CancellationToken cancellationToken)
        {
            await BestEffortAsync(() => _serviceManager.StopServiceAsync(appName, cancellationToken));
            await BestEffortAsync(() => _serviceManager.DisableServiceAsync(appName, cancellationToken));
            File.Delete(_scope.GetUnitFilePath(appName));
            await _serviceManager.DaemonReloadAsync(cancellationToken);
        }

        private async Task BestEffortAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _outputWriter.WriteError($"Warning: {ex.Message}");
            }
        }
    }
}
