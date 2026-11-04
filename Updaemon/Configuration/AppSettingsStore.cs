using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Configuration
{
    /// <summary>
    /// Loads and saves an app's config entry together with its secret values.
    /// </summary>
    public class AppSettingsStore
    {
        private readonly IConfigManager _configManager;
        private readonly ISecretsManager _secretsManager;

        public AppSettingsStore(IConfigManager configManager, ISecretsManager secretsManager)
        {
            _configManager = configManager;
            _secretsManager = secretsManager;
        }

        public async Task<AppValues> LoadValuesAsync(InstalledApp app, CancellationToken cancellationToken = default)
        {
            return new AppValues(app, await _secretsManager.GetAppSecretsAsync(app.Name, cancellationToken));
        }

        /// <summary>
        /// Saves the app's config entry and its secrets. Secrets go first, so a value moved from the
        /// plain settings into the secrets is never stored in neither.
        /// </summary>
        public async Task SaveAsync(AppValues values, CancellationToken cancellationToken = default)
        {
            await _secretsManager.SaveAppSecretsAsync(values.App.Name, values.App.RunAs, values.Secrets, cancellationToken);
            await _configManager.SaveAppAsync(values.App, cancellationToken);
        }
    }
}
