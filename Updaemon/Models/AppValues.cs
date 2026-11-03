namespace Updaemon.Models
{
    /// <summary>
    /// An app's setting values: plain ones, kept in its config entry, and secret ones, kept in its
    /// secrets file. The one place that decides which of the two a value belongs in and which required
    /// settings are missing.
    /// </summary>
    public sealed class AppValues
    {
        public AppValues(InstalledApp app, Dictionary<string, string> secrets)
        {
            App = app;
            Secrets = secrets;
        }

        public InstalledApp App { get; }

        /// <summary>The plain values: the app's own <see cref="InstalledApp.Settings"/>.</summary>
        public Dictionary<string, string> Settings => App.Settings;

        public Dictionary<string, string> Secrets { get; }

        /// <summary>
        /// Stores a value. A setting the manifest declares is secret exactly when the manifest says so.
        /// Any other key is secret when <paramref name="secret"/> is set or it is already stored as a
        /// secret, so changing a secret value never moves it into the readable settings file.
        /// </summary>
        public void Set(AppManifest manifest, string key, string value, bool secret = false)
        {
            bool isSecret = manifest.Settings.ContainsKey(key)
                ? manifest.IsSecret(key)
                : secret || Secrets.ContainsKey(key);

            Settings.Remove(key);
            Secrets.Remove(key);
            (isSecret ? Secrets : Settings)[key] = value;
        }

        /// <summary>Removes a value from wherever it is stored. Returns whether there was one.</summary>
        public bool Remove(string key) => Settings.Remove(key) | Secrets.Remove(key);

        /// <summary>Moves plain values that <paramref name="manifest"/> declares secret into the secrets.</summary>
        public void ConformTo(AppManifest manifest)
        {
            foreach (string key in Settings.Keys.Where(manifest.IsSecret).ToList())
            {
                Secrets[key] = Settings[key];
                Settings.Remove(key);
            }
        }

        /// <summary>Required settings of <paramref name="manifest"/> that have no value, in name order.</summary>
        public IReadOnlyList<string> MissingRequired(AppManifest manifest) =>
            manifest.MissingRequiredSettings(Settings.Keys.Concat(Secrets.Keys));
    }
}
