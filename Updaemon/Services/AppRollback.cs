using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Makes a retained version current again. The one rule for a version that failed its health
    /// check and for one rolled back from by hand: it is skipped, so update does not offer it again.
    /// </summary>
    public class AppRollback
    {
        private readonly IServiceDeployer _serviceDeployer;
        private readonly AppSettingsStore _settingsStore;
        private readonly IConfigManager _configManager;

        public AppRollback(IServiceDeployer serviceDeployer, AppSettingsStore settingsStore, IConfigManager configManager)
        {
            _serviceDeployer = serviceDeployer;
            _settingsStore = settingsStore;
            _configManager = configManager;
        }

        /// <summary>
        /// Activates the retained version <paramref name="to"/> in place of <paramref name="from"/> and
        /// records the switch (see <see cref="InstalledApp.RecordSwitch"/>). Does not restart the service.
        /// </summary>
        /// <exception cref="InvalidOperationException"><paramref name="to"/> is not retained, or needs a setting without a value.</exception>
        public async Task SwitchAsync(InstalledApp app, Version from, Version to, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            DeployableVersion target = await _serviceDeployer.OpenRetainedAsync(app.Name, to, cancellationToken);
            AppValues values = await _settingsStore.LoadValuesAsync(app, cancellationToken);
            values.EnsureComplete(target.Manifest, to, from, $"updaemon rollback {app.Name} {to}");

            await _serviceDeployer.ActivateAsync(app, target, reachable, cancellationToken);
            app.RecordSwitch(from, to);
            await _configManager.SaveAppAsync(app, cancellationToken);
        }
    }
}
