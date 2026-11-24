using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'remove' command: stops an app and deletes everything updaemon made for it, or takes one
    /// extension out of it.
    /// </summary>
    public class RemoveCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly AppSwitcher _switcher;
        private readonly UpdateRunLog _runLog;
        private readonly IOutputWriter _outputWriter;
        private readonly UpdaemonScope _scope;

        public RemoveCommand(IConfigManager configManager, IServiceDeployer serviceDeployer, AppSwitcher switcher, UpdateRunLog runLog, IOutputWriter outputWriter, UpdaemonScope scope)
        {
            _configManager = configManager;
            _serviceDeployer = serviceDeployer;
            _switcher = switcher;
            _runLog = runLog;
            _outputWriter = outputWriter;
            _scope = scope;
        }

        public string Name => "remove";

        public string Description => "Remove an app, or an extension of one";

        public string Usage => "updaemon remove <app>[/<extension>] [--purge]";

        public bool ChangesState(string[] args) => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args, "--purge");
            if (parser.GetUnknownFlags("--purge") is [string unknown, ..])
            {
                return _outputWriter.FailUnknownOption(unknown);
            }

            if (parser.Positionals is not [string name])
            {
                return _outputWriter.FailUsage(this);
            }

            SelectedPackage? package = await AppSelection.FindAsync(name, _configManager, _outputWriter, cancellationToken);
            if (package == null)
            {
                return 1;
            }

            bool purge = parser.HasFlag("--purge");
            if (package.Extension == null)
            {
                return await RemoveAppAsync(package.App, purge, cancellationToken);
            }

            return purge
                ? _outputWriter.Fail($"--purge does not go with an extension, which has no data of its own. Use 'updaemon remove {name}'.")
                : await RemoveExtensionAsync(package.App, package.Extension, cancellationToken);
        }

        private async Task<int> RemoveAppAsync(InstalledApp app, bool purge, CancellationToken cancellationToken)
        {
            try
            {
                // Puts back what a killed run left, so what is removed is what is there.
                await _switcher.PrepareAsync(app, cancellationToken);
            }
            catch (Exception ex) when (app.Unfinished != null && ex is not (OperationCanceledException or ServiceManagerUnreachableException))
            {
                _outputWriter.WriteError($"Warning: {ex.Message}");
            }

            ReachableServiceManager reachable = await _serviceDeployer.ReachServiceManagerAsync(app.Name, cancellationToken: cancellationToken);
            await _serviceDeployer.RemoveAsync(app, purge, reachable, cancellationToken);
            await _configManager.RemoveAppAsync(app.Name, cancellationToken);
            string[] extensions = app.Extensions.Select(extension => app.NameOf(extension).ToString()).ToArray();
            string removed = extensions.Length switch
            {
                0 => app.Name,
                1 => $"{app.Name}, its extension {extensions[0]}",
                _ => $"{app.Name}, its extensions {string.Join(", ", extensions)}",
            };
            _outputWriter.WriteLine(purge
                ? $"Removed {removed} and its data."
                : $"Removed {removed}. {(extensions.Length == 0 ? "Its" : $"{app.Name}'s")} data is kept in {_scope.GetDataDirectory(app.Name)}.");
            await ForgetFailuresAsync(extensions.Append(app.Name), cancellationToken);
            return 0;
        }

        private async Task<int> RemoveExtensionAsync(InstalledApp host, InstalledExtension selected, CancellationToken cancellationToken)
        {
            string name = host.NameOf(selected).ToString();
            PreparedApp prepared = await _switcher.PrepareAsync(host, cancellationToken);
            if (host.FindExtension(selected.Id) is not InstalledExtension extension)
            {
                // A killed install of it, taken out again by the preparing.
                return _outputWriter.FailNotInstalled(name);
            }

            string? problem = await _switcher.RemoveExtensionAsync(prepared, extension, cancellationToken);
            _outputWriter.WriteLine($"Removed {name}.");
            await ForgetFailuresAsync(new[] { name }, cancellationToken);
            return problem == null ? 0 : _outputWriter.Fail($"{problem}. {name} is removed. {ServiceDeployer.SeeLogs(host.Name)}");
        }

        /// <summary>What is removed no longer fails; the removal stands without this.</summary>
        private async Task ForgetFailuresAsync(IEnumerable<string> names, CancellationToken cancellationToken)
        {
            try
            {
                await _runLog.ForgetFailuresAsync(names, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _outputWriter.WriteError($"Warning: the last run's failures still name it in 'updaemon status': {ex.Message}");
            }
        }

        public string GetDetailedHelp()
        {
            return """
                Remove Command

                Usage:
                  updaemon remove <app> [--purge]
                  updaemon remove <app>/<extension>

                Description:
                  Stops the app's service and deletes its unit, its command, all its
                  versions, its extensions, its settings and its secrets. Its data directory
                  is kept, so a reinstall picks up where it left off; --purge deletes that
                  too, and on macOS its log files.

                  <app>/<extension> removes only that extension and restarts the app, unless it
                  is not a service, is set to manual restart or is disabled.

                Examples:
                  updaemon remove byteshelf
                  updaemon remove byteshelf --purge
                  updaemon remove byteshelf/images
                """;
        }
    }
}
