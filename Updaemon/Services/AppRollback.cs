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
        /// All or nothing: a switch that cannot be recorded, on a full disk say, is switched back.
        /// </summary>
        /// <exception cref="InvalidOperationException"><paramref name="to"/> is not retained, or needs a setting without a value.</exception>
        public async Task SwitchAsync(InstalledApp app, Version from, Version to, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            await ActivateAsync(app, from, to, reachable, cancellationToken);
            try
            {
                await RecordAsync(app, from, to, cancellationToken);
            }
            catch (Exception ex)
            {
                try
                {
                    await _serviceDeployer.ActivateAsync(app, await _serviceDeployer.OpenRetainedAsync(app.Name, from, CancellationToken.None), reachable, CancellationToken.None);
                }
                catch (Exception undo)
                {
                    throw new InvalidOperationException($"{ex.Message.TrimEnd('.')}, and switching back to {from} failed: {undo.Message}", ex);
                }

                throw;
            }
        }

        /// <summary>
        /// After <paramref name="broken"/> failed its health check: makes <paramref name="previous"/> current
        /// again and skips <paramref name="broken"/>. Does not restart the service. <paramref name="previous"/>
        /// stays current even when the skip cannot be recorded, as <paramref name="broken"/> is known not
        /// to run: returns why it could not, or null.
        /// </summary>
        /// <exception cref="InvalidOperationException"><paramref name="previous"/> is not retained, or needs a setting without a value.</exception>
        public async Task<string?> FallBackAsync(InstalledApp app, Version broken, Version previous, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            await ActivateAsync(app, broken, previous, reachable, cancellationToken);
            try
            {
                await RecordAsync(app, broken, previous, cancellationToken);
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ex.Message;
            }
        }

        private async Task ActivateAsync(InstalledApp app, Version from, Version to, ReachableServiceManager reachable, CancellationToken cancellationToken)
        {
            DeployableVersion target = await _serviceDeployer.OpenRetainedAsync(app.Name, to, cancellationToken);
            AppValues values = await _settingsStore.LoadValuesAsync(app, cancellationToken);
            values.EnsureComplete(target.Manifest, to, from, $"updaemon rollback {app.Name} {to}");
            await _serviceDeployer.ActivateAsync(app, target, reachable, cancellationToken);
        }

        /// <summary>Saves the switch first, so <paramref name="app"/> records it only once config.json does.</summary>
        private async Task RecordAsync(InstalledApp app, Version from, Version to, CancellationToken cancellationToken)
        {
            await _configManager.SaveAppAsync(app.AfterSwitch(from, to), cancellationToken);
            app.RecordSwitch(from, to);
        }
    }
}
