namespace Updaemon.Models
{
    /// <summary>
    /// Proof that no killed run left <see cref="App"/> between two versions: switching an installed app's
    /// versions, applying its settings and writing its files anew all take one. Only
    /// <see cref="Services.AppSwitcher.PrepareAsync"/> makes one, so a command cannot build on a half-done
    /// switch. Good for one change: a failed one leaves <see cref="App"/> as no longer saved.
    /// </summary>
    public sealed class PreparedApp
    {
        private bool _used;

        internal PreparedApp(InstalledApp app)
        {
            App = app;
        }

        public InstalledApp App { get; }

        /// <summary>Called by what changes <see cref="App"/>, before it does.</summary>
        /// <exception cref="InvalidOperationException">A change was begun with this already, or the app is in the middle of one.</exception>
        internal void BeginChange()
        {
            if (_used || App.Unfinished != null)
            {
                throw new InvalidOperationException($"{App.Name} was changed since it was prepared; prepare it again.");
            }

            _used = true;
        }
    }
}
