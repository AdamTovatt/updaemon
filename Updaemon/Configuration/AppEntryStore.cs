using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Configuration
{
    /// <summary>
    /// Loads and saves an app's entry and its secret values, each by itself or both.
    /// </summary>
    public class AppEntryStore
    {
        private readonly IConfigManager _configManager;
        private readonly ISecretsManager _secretsManager;

        public AppEntryStore(IConfigManager configManager, ISecretsManager secretsManager)
        {
            _configManager = configManager;
            _secretsManager = secretsManager;
        }

        public Task<InstalledApp?> FindEntryAsync(string appName, CancellationToken cancellationToken = default) => _configManager.GetAppAsync(appName, cancellationToken);

        public async Task<AppValues> LoadValuesAsync(InstalledApp app, CancellationToken cancellationToken = default)
        {
            return AppValues.Stored(app, await _secretsManager.GetAppSecretsAsync(app.Name, cancellationToken));
        }

        /// <summary>
        /// Saves the secrets, then the entry: a value moved from the plain settings into the secrets is never
        /// stored in neither.
        /// </summary>
        /// <returns>The entry as it is saved.</returns>
        public async Task<InstalledApp> SaveAsync(AppValues values, UnfinishedChange? unfinished = null, CancellationToken cancellationToken = default)
        {
            await SaveSecretsAsync(values, cancellationToken);
            return await SaveEntryAsync(values.App, unfinished, cancellationToken);
        }

        /// <summary>Nothing when they are stored already.</summary>
        public async Task SaveSecretsAsync(AppValues values, CancellationToken cancellationToken = default)
        {
            if (values.SecretsChanged)
            {
                await _secretsManager.SaveAppSecretsAsync(values.App.Name, values.App.RunAs, values.Secrets, cancellationToken);
                values.SecretsAreStored();
            }
        }

        /// <param name="unfinished">What the entry is saved with as <see cref="InstalledApp.Unfinished"/>.</param>
        /// <returns>The entry as it is saved, a copy of <paramref name="entry"/>.</returns>
        public async Task<InstalledApp> SaveEntryAsync(InstalledApp entry, UnfinishedChange? unfinished, CancellationToken cancellationToken = default)
        {
            InstalledApp saved = entry.Copy();
            saved.Unfinished = unfinished;
            await _configManager.SaveAppAsync(saved, cancellationToken);
            return saved;
        }
    }
}
