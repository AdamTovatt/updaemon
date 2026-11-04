using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// For commands that take <c>[app]</c>: every installed app, or the one named.
    /// </summary>
    public static class AppSelection
    {
        /// <summary>
        /// Every installed app when <paramref name="positionals"/> is empty, else the one it names. When it
        /// names more than one, or one that is not installed, writes the failure and returns null; the
        /// command then exits with 1.
        /// </summary>
        public static async Task<IReadOnlyList<InstalledApp>?> SelectAsync(
            ICommand command, IReadOnlyList<string> positionals, IConfigManager configManager, IOutputWriter outputWriter, CancellationToken cancellationToken)
        {
            switch (positionals)
            {
                case []:
                    return await configManager.GetAllAppsAsync(cancellationToken);
                case [string appName]:
                    InstalledApp? app = await configManager.GetAppAsync(appName, cancellationToken);
                    if (app == null)
                    {
                        outputWriter.Fail($"'{appName}' is not installed.");
                        return null;
                    }

                    return new[] { app };
                default:
                    outputWriter.FailUsage(command);
                    return null;
            }
        }
    }
}
