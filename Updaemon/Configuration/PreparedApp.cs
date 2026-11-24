using Updaemon.Models;

namespace Updaemon.Configuration
{
    /// <summary>
    /// Proof that no killed run left <see cref="App"/> in the middle of a change. Switching an installed app's
    /// versions, changing its settings and writing its files anew all take the <see cref="AppChange"/> begun with
    /// one. Only <see cref="Services.AppPreparer"/> and a change that stands make one, so a command cannot build
    /// on a half-done change. Good for one change.
    /// </summary>
    /// <remarks>
    /// Not needed for <see cref="InstalledApp.AutoRestart"/>, which no file or service follows: turning restarts
    /// off must not first end an earlier change, which may restart the service.
    /// </remarks>
    public sealed class PreparedApp
    {
        private readonly AppEntryStore _store;
        private bool _used;

        internal PreparedApp(InstalledApp app, AppEntryStore store)
        {
            App = app;
            _store = store;
        }

        /// <summary>The app's entry as it is saved. Not to be edited: <see cref="AppChange.After"/> is, and no change begins with one that was.</summary>
        public InstalledApp App { get; }

        /// <summary>Hands out a copy of the entry, with its values, to edit. Saves nothing.</summary>
        /// <exception cref="InvalidOperationException">A change was begun with this already, or <see cref="App"/> is not what is saved.</exception>
        public async Task<AppChange> BeginChangeAsync(CancellationToken cancellationToken = default)
        {
            InstalledApp? saved = await _store.FindEntryAsync(App.Name, cancellationToken);
            if (_used || saved == null || !saved.IsSameAs(App))
            {
                throw new InvalidOperationException($"{App.Name} was changed since it was prepared; prepare it again.");
            }

            AppValues after = await _store.LoadValuesAsync(App.Copy(), cancellationToken);
            _used = true;
            return new AppChange(App, after, _store);
        }
    }
}
