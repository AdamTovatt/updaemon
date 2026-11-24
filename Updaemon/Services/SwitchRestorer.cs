using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Puts back what an app's <see cref="UnfinishedSwitch"/> says was there before, for
    /// <see cref="AppSwitcher"/>: after a switch that failed, and after one whose run was killed. Saves
    /// nothing, and needs neither the network nor a plugin. What it has begun it ends, so it takes no
    /// cancellation.
    /// </summary>
    public class SwitchRestorer
    {
        private readonly VersionStore _versions;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IServiceManager _serviceManager;

        public SwitchRestorer(VersionStore versions, IServiceDeployer serviceDeployer, IServiceManager serviceManager)
        {
            _versions = versions;
            _serviceDeployer = serviceDeployer;
            _serviceManager = serviceManager;
        }

        /// <summary>
        /// Makes what <see cref="UnfinishedSwitch.PutBack"/> names current again, all of it even when one fails,
        /// and takes out an extension that was not there before. Does not restart and does not save.
        /// </summary>
        /// <exception cref="InvalidOperationException">Some of it could not be; the message says what.</exception>
        public async Task RestoreVersionsAsync(InstalledApp app, ReachableServiceManager reachable)
        {
            List<string> problems = new List<string>();
            foreach (VersionBefore before in app.Unfinished!.PutBack)
            {
                try
                {
                    await RestoreVersionAsync(app, before, reachable);
                }
                catch (Exception ex)
                {
                    problems.Add(ex.Message.TrimEnd('.'));
                }
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(string.Join("; ", problems) + ".");
            }
        }

        private async Task RestoreVersionAsync(InstalledApp app, VersionBefore before, ReachableServiceManager reachable)
        {
            if (before.Extension == null)
            {
                DeployableVersion version = await _serviceDeployer.OpenRetainedAsync(app.Name, before.Version!, CancellationToken.None);
                await _serviceDeployer.ActivateAsync(app, version, reachable, CancellationToken.None);
                if (app.Unfinished!.RemovedService is ServiceBefore service)
                {
                    await RestoreServiceAsync(app.Name, service);
                }

                return;
            }

            PackageName name = before.NameIn(app);
            if (before.Version != null)
            {
                await _versions.PointAsync(name, before.Version, CancellationToken.None);
                return;
            }

            _versions.Unlink(name);
            app.Extensions.RemoveAll(extension => extension.Id == before.Extension);
            _versions.TryDeleteAll(name);
        }

        /// <summary>Activating a version that is no service stopped and removed this one, and activating its version only wrote its unit again.</summary>
        private async Task RestoreServiceAsync(string appName, ServiceBefore before)
        {
            if (before.Enabled && !await _serviceManager.IsEnabledAsync(appName, CancellationToken.None))
            {
                await _serviceManager.EnableServiceAsync(appName, CancellationToken.None);
            }

            if (before.Running && !(await _serviceManager.GetStateAsync(appName, CancellationToken.None)).IsActive)
            {
                await _serviceManager.StartServiceAsync(appName, CancellationToken.None);
            }
        }

        /// <summary>Restarts the service if the unfinished switch does and it still is one, without a health check. Returns what failed, or null.</summary>
        public async Task<string?> RestartAsync(InstalledApp app)
        {
            if (!app.Unfinished!.Restart || !_serviceDeployer.HasService(app.Name))
            {
                return null;
            }

            try
            {
                await _serviceManager.RestartServiceAsync(app.Name, CancellationToken.None);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
