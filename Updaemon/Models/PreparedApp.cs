namespace Updaemon.Models
{
    /// <summary>
    /// Proof that no killed run left <see cref="App"/> between two versions: every method that switches its
    /// versions or applies its settings takes one. Only <see cref="Services.AppSwitcher.PrepareAsync"/> makes
    /// one, so a command cannot build on a half-done switch.
    /// </summary>
    public sealed class PreparedApp
    {
        internal PreparedApp(InstalledApp app)
        {
            App = app;
        }

        public InstalledApp App { get; }
    }
}
