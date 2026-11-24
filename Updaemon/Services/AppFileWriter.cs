using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.IO;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Writes and removes what updaemon generates for an app from its manifest and settings: the
    /// data directory, the folder of extensions, the settings file, the command wrapper on PATH, and the
    /// service's wrapper and unit.
    /// </summary>
    public class AppFileWriter
    {
        private readonly UpdaemonScope _scope;
        private readonly IFilePermissionManager _filePermissionManager;
        private readonly IUnitFileManager _unitFileManager;
        private readonly IServiceManager _serviceManager;
        private readonly IOutputWriter _outputWriter;

        public AppFileWriter(
            UpdaemonScope scope,
            IFilePermissionManager filePermissionManager,
            IUnitFileManager unitFileManager,
            IServiceManager serviceManager,
            IOutputWriter outputWriter)
        {
            _scope = scope;
            _filePermissionManager = filePermissionManager;
            _unitFileManager = unitFileManager;
            _serviceManager = serviceManager;
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

            string? owner = AppWrapper.ReadOwner(path);
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
        /// Generates the app's files for <paramref name="manifest"/>, removing what any of
        /// <paramref name="earlier"/> had and <paramref name="manifest"/> has not (a renamed
        /// command, a service that is gone). Does not start or restart the service.
        /// </summary>
        public async Task WriteAsync(InstalledApp app, AppManifest manifest, IReadOnlyCollection<AppManifest> earlier, CancellationToken cancellationToken)
        {
            string dataDirectory = _scope.GetDataDirectory(app.Name);
            string secretsFilePath = _scope.GetSecretsFilePath(app.Name);
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

            string settingsFilePath = _scope.GetSettingsFilePath(app.Name);
            await EnvFile.WriteAsync(settingsFilePath, settings, AtomicFile.Readable, cancellationToken);

            Dictionary<string, string> variables = new Dictionary<string, string> { [AppManifest.DataDirectoryVariable] = dataDirectory };
            string extensionsDirectory = _scope.GetExtensionsDirectory(app.Name);
            if (manifest.Extensions != null)
            {
                variables[manifest.Extensions.Variable!] = extensionsDirectory;
                Directory.CreateDirectory(extensionsDirectory);
                await _filePermissionManager.MakeTreeReadableAsync(extensionsDirectory, cancellationToken);
            }
            else if (earlier.Any(had => had.Extensions != null) && Directory.Exists(extensionsDirectory) && !Directory.EnumerateFileSystemEntries(extensionsDirectory).Any())
            {
                Directory.Delete(extensionsDirectory);
            }

            string currentLink = _scope.GetCurrentLinkPath(app.Name);
            string executablePath = Path.Combine(currentLink, manifest.Executable);

            foreach (string command in earlier.Select(had => had.Command).OfType<string>().Where(command => command != manifest.Command))
            {
                DeleteOwnWrapper(command, app.Name);
            }

            if (manifest.Command != null)
            {
                string wrapper = AppWrapper.ForCommand(app.Name, executablePath, settingsFilePath, secretsFilePath, variables);
                await AtomicFile.WriteAsync(_scope.GetCommandPath(manifest.Command), wrapper, AtomicFile.Executable, cancellationToken);
            }

            if (manifest.Service != null)
            {
                string wrapperPath = _scope.GetServiceWrapperPath(app.Name);
                string wrapper = AppWrapper.ForService(app.Name, executablePath, settingsFilePath, secretsFilePath, variables);
                await AtomicFile.WriteAsync(wrapperPath, wrapper, AtomicFile.Executable, cancellationToken);

                ServiceUnit unit = new ServiceUnit
                {
                    AppName = app.Name,
                    WorkingDirectory = currentLink,
                    WrapperPath = wrapperPath,
                    Arguments = app.Args ?? manifest.Service.Args,
                    RunAs = app.RunAs,
                };

                await _unitFileManager.WriteUnitFileAsync(unit, cancellationToken);
                await _serviceManager.DaemonReloadAsync(cancellationToken);
            }
            else if (earlier.Any(had => had.Service != null))
            {
                await RemoveServiceAsync(app.Name, cancellationToken);
            }
        }

        /// <summary>Stops and deletes the app's service unit if it has one, and deletes its commands and settings file.</summary>
        public async Task RemoveAsync(string appName, CancellationToken cancellationToken)
        {
            if (File.Exists(_scope.GetUnitFilePath(appName)))
            {
                await RemoveServiceAsync(appName, cancellationToken);
            }

            RemoveCommandsAndSettings(appName);
        }

        /// <summary>
        /// Deletes every wrapper on PATH the app owns, whichever of its versions made it, and its settings file;
        /// leaves its service unit.
        /// </summary>
        public void RemoveCommandsAndSettings(string appName)
        {
            if (Directory.Exists(_scope.BinDirectory))
            {
                // A link to a wrapper is not updaemon's.
                foreach (FileInfo file in new DirectoryInfo(_scope.BinDirectory).GetFiles().Where(file => file.LinkTarget == null))
                {
                    DeleteOwnWrapper(file.Name, appName);
                }
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
            if (AppWrapper.ReadOwner(path) == appName)
            {
                File.Delete(path);
            }
        }

        private async Task RemoveServiceAsync(string appName, CancellationToken cancellationToken)
        {
            string unitPath = _scope.GetUnitFilePath(appName);
            if (File.Exists(unitPath))
            {
                await BestEffortAsync(() => _serviceManager.StopServiceAsync(appName, cancellationToken));
                await BestEffortAsync(() => _serviceManager.DisableServiceAsync(appName, cancellationToken));
                File.Delete(unitPath);
            }

            // Also without the unit: a run killed in here left the rest.
            Files.DeleteIfExists(_scope.GetServiceWrapperPath(appName));
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
