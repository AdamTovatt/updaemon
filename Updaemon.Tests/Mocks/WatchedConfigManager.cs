using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Tests.Mocks
{
    /// <summary>A real config, with a call around each save of an app; the next ones can be made to fail, as on a disk that is full for a moment.</summary>
    public sealed class WatchedConfigManager : IConfigManager
    {
        private readonly IConfigManager _inner;

        public WatchedConfigManager(IConfigManager inner)
        {
            _inner = inner;
        }

        private readonly List<Func<InstalledApp, bool>> _failing = new List<Func<InstalledApp, bool>>();

        /// <summary>The next save of an app that <paramref name="which"/> is true of fails, as on a full disk: one for each call.</summary>
        public void FailNext(Func<InstalledApp, bool> which) => _failing.Add(which);

        /// <summary>That of what makes a switch that restarts first stand, or of everything being back.</summary>
        public void FailNextWithNothingLeft() => FailNext(app => app.Unfinished == null);

        /// <summary>How many saves are still to fail.</summary>
        public int StillToFail => _failing.Count;

        /// <summary>Called with the app before each save, to look at what is there then.</summary>
        public Action<InstalledApp>? OnSave { get; set; }

        /// <summary>Called with the app after each save that worked.</summary>
        public Action<InstalledApp>? OnSaved { get; set; }

        public async Task SaveAppAsync(InstalledApp app, CancellationToken cancellationToken = default)
        {
            OnSave?.Invoke(app);
            if (_failing.Find(which => which(app)) is Func<InstalledApp, bool> failing)
            {
                _failing.Remove(failing);
                throw new IOException("disk full");
            }

            await _inner.SaveAppAsync(app, cancellationToken);
            OnSaved?.Invoke(app);
        }

        public Task<UpdaemonConfig> LoadConfigAsync(CancellationToken cancellationToken = default) => _inner.LoadConfigAsync(cancellationToken);

        public Task SaveConfigAsync(UpdaemonConfig config, CancellationToken cancellationToken = default) => _inner.SaveConfigAsync(config, cancellationToken);

        public Task<InstalledApp?> GetAppAsync(string name, CancellationToken cancellationToken = default) => _inner.GetAppAsync(name, cancellationToken);

        public Task<IReadOnlyList<InstalledApp>> GetAllAppsAsync(CancellationToken cancellationToken = default) => _inner.GetAllAppsAsync(cancellationToken);

        public Task RemoveAppAsync(string name, CancellationToken cancellationToken = default) => _inner.RemoveAppAsync(name, cancellationToken);

        public Task AddOrUpdatePluginAsync(InstalledPluginInfo pluginInfo, CancellationToken cancellationToken = default) => _inner.AddOrUpdatePluginAsync(pluginInfo, cancellationToken);

        public Task<InstalledPluginInfo?> GetPluginAsync(string alias, CancellationToken cancellationToken = default) => _inner.GetPluginAsync(alias, cancellationToken);

        public Task<IReadOnlyDictionary<string, InstalledPluginInfo>> GetAllPluginsAsync(CancellationToken cancellationToken = default) => _inner.GetAllPluginsAsync(cancellationToken);

        public Task RemovePluginAsync(string alias, CancellationToken cancellationToken = default) => _inner.RemovePluginAsync(alias, cancellationToken);
    }
}
