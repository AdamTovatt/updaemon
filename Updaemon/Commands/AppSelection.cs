using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Commands
{
    /// <summary>
    /// Finds what a command's arguments name. A name that is not installed, or not one the command takes, is
    /// reported here; the command then exits with 1.
    /// </summary>
    public static class AppSelection
    {
        /// <summary>For commands that take an app: the one named, or null.</summary>
        public static async Task<InstalledApp?> FindAppAsync(
            ICommand command, string name, IConfigManager configManager, IOutputWriter outputWriter, CancellationToken cancellationToken)
        {
            if (ExtensionName.TryParse(name) is ExtensionName extension)
            {
                outputWriter.Fail($"'{name}' names an extension, which '{command.Name}' does not work on. For its host, use '{command.Usage.Replace("<app>", extension.Host)}'.");
                return null;
            }

            return (await FindAsync(name, configManager, outputWriter, cancellationToken))?.App;
        }

        /// <summary>For commands that take an app or an extension (<c>&lt;app&gt;/&lt;id&gt;</c>): the one named, or null.</summary>
        public static async Task<SelectedPackage?> FindAsync(string name, IConfigManager configManager, IOutputWriter outputWriter, CancellationToken cancellationToken)
        {
            ExtensionName? extensionName = ExtensionName.TryParse(name);
            InstalledApp? app = await configManager.GetAppAsync(extensionName?.Host ?? name, cancellationToken);
            InstalledExtension? extension = extensionName == null ? null : app?.FindExtension(extensionName.Id);
            if (app == null || (extensionName != null && extension == null))
            {
                outputWriter.FailNotInstalled(name);
                return null;
            }

            return new SelectedPackage(app, extension);
        }

        /// <summary>
        /// For commands that change it: ends what an earlier run left unfinished, then finds it in the entry as that
        /// leaves it. An extension that a killed run was installing is taken out again, and so not installed.
        /// </summary>
        /// <exception cref="InvalidOperationException">What was left unfinished cannot be ended; see <see cref="AppPreparer.PrepareAsync"/>.</exception>
        public static async Task<PreparedPackage?> PrepareAsync(
            string name, IConfigManager configManager, AppPreparer preparer, IOutputWriter outputWriter, CancellationToken cancellationToken)
        {
            ExtensionName? extensionName = ExtensionName.TryParse(name);
            InstalledApp? app = await configManager.GetAppAsync(extensionName?.Host ?? name, cancellationToken);
            if (app != null)
            {
                PreparedApp prepared = await preparer.PrepareAsync(app, cancellationToken);
                InstalledExtension? extension = extensionName == null ? null : prepared.App.FindExtension(extensionName.Id);
                if (extensionName == null || extension != null)
                {
                    return new PreparedPackage(prepared, extension);
                }
            }

            outputWriter.FailNotInstalled(name);
            return null;
        }

        /// <summary>
        /// For commands that take <c>[app]</c>: every installed app when <paramref name="positionals"/> is
        /// empty, else what it names. Null when it names more than one, or one that is not installed.
        /// </summary>
        public static async Task<IReadOnlyList<SelectedPackage>?> SelectAsync(
            ICommand command,
            IReadOnlyList<string> positionals,
            IConfigManager configManager,
            IOutputWriter outputWriter,
            CancellationToken cancellationToken)
        {
            switch (positionals)
            {
                case []:
                    return (await configManager.GetAllAppsAsync(cancellationToken)).Select(app => new SelectedPackage(app, null)).ToList();
                case [string name]:
                    return await FindAsync(name, configManager, outputWriter, cancellationToken) is SelectedPackage package ? new[] { package } : null;
                default:
                    outputWriter.FailUsage(command);
                    return null;
            }
        }
    }
}
