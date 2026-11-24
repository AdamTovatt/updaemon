using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Puts back what an app's <see cref="UnfinishedChange"/> says was there before, or has its files follow
    /// what stands: for <see cref="AppSwitcher"/> after a switch that failed, and for
    /// <see cref="AppPreparer"/> after a change that a run left unfinished. Saves nothing, and needs neither the
    /// network nor a plugin. What it has begun it ends, so it takes no cancellation.
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
        /// Makes what <paramref name="putBack"/> names current again, all of it even when one fails, and deletes
        /// the versions of an extension that is not in the entry. Once all of it is, a service that was removed is
        /// as it was. Does not restart.
        /// </summary>
        /// <param name="app">The entry from before the change.</param>
        /// <exception cref="InvalidOperationException">Some of it could not be; the message says what.</exception>
        public async Task RestoreVersionsAsync(InstalledApp app, UnfinishedChange.PutBack putBack, ReachableServiceManager reachable)
        {
            List<string> problems = new List<string>();
            foreach (VersionBefore before in putBack.Versions)
            {
                await TryAsync(() => RestoreVersionAsync(app, before, putBack, reachable), problems);
            }

            if (problems.Count == 0 && putBack.RemovedService is ServiceBefore service)
            {
                // Last: it starts with what is there then.
                await TryAsync(() => RestoreServiceAsync(app.Name, service), problems);
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(string.Join("; ", problems) + ".");
            }
        }

        private static async Task TryAsync(Func<Task> step, List<string> problems)
        {
            try
            {
                await step();
            }
            catch (Exception ex)
            {
                problems.Add(ex.Message.TrimEnd('.'));
            }
        }

        private async Task RestoreVersionAsync(InstalledApp app, VersionBefore before, UnfinishedChange.PutBack putBack, ReachableServiceManager reachable)
        {
            if (before.Extension == null)
            {
                DeployableVersion version = await _serviceDeployer.OpenRetainedAsync(app.Name, before.Version!, CancellationToken.None);
                await _serviceDeployer.ActivateAsync(app, version, reachable, putBack.NewManifest, CancellationToken.None);
                return;
            }

            PackageName name = before.NameIn(app);
            if (before.Version != null)
            {
                await _versions.PointAsync(name, before.Version, CancellationToken.None);
                return;
            }

            _versions.Unlink(name);
            if (app.Find(name) == null)
            {
                _versions.TryDeleteAll(name);
            }
        }

        /// <summary>Generates the app's files for its current version and <paramref name="app"/>'s settings.</summary>
        public async Task WriteFilesAsync(InstalledApp app, ReachableServiceManager reachable) =>
            await _serviceDeployer.ActivateAsync(app, await _serviceDeployer.OpenCurrentAsync(app.Name, CancellationToken.None), reachable, cancellationToken: CancellationToken.None);

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

        /// <summary>Restarts the service if there is one, without a health check. Returns what failed, or null.</summary>
        public async Task<string?> RestartAsync(string appName)
        {
            if (!_serviceDeployer.HasService(appName))
            {
                return null;
            }

            try
            {
                await _serviceManager.RestartServiceAsync(appName, CancellationToken.None);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
