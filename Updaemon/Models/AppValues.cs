namespace Updaemon.Models
{
    /// <summary>
    /// An app's setting values: plain ones, kept in its config entry, and secret ones, kept in its
    /// secrets file. The one place that decides which of the two a value belongs in and which required
    /// settings are missing.
    /// </summary>
    public sealed class AppValues
    {
        /// <summary>The secrets as they are stored; null when that is not known.</summary>
        private Dictionary<string, string>? _storedSecrets;

        public AppValues(InstalledApp app, Dictionary<string, string> secrets)
        {
            App = app;
            Secrets = secrets;
        }

        /// <summary>Those of an app whose secrets are stored as <paramref name="secrets"/>.</summary>
        public static AppValues Stored(InstalledApp app, Dictionary<string, string> secrets)
        {
            AppValues values = new AppValues(app, secrets);
            values.SecretsAreStored();
            return values;
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
        /// <exception cref="InvalidOperationException">Updaemon sets <paramref name="key"/> itself.</exception>
        public void Set(AppManifest manifest, string key, string value, bool secret = false)
        {
            EnsureNotSetByUpdaemon(manifest, key);
            bool isSecret = manifest.Settings.ContainsKey(key)
                ? manifest.IsSecret(key)
                : secret || Secrets.ContainsKey(key);

            Settings.Remove(key);
            Secrets.Remove(key);
            (isSecret ? Secrets : Settings)[key] = value;
        }

        /// <summary>Removes a value from wherever it is stored. Returns whether there was one.</summary>
        /// <exception cref="InvalidOperationException">Updaemon sets <paramref name="key"/> itself.</exception>
        public bool Remove(AppManifest manifest, string key)
        {
            EnsureNotSetByUpdaemon(manifest, key);
            return Settings.Remove(key) | Secrets.Remove(key);
        }

        /// <summary>Whether <see cref="Secrets"/> may differ from what is stored.</summary>
        public bool SecretsChanged =>
            _storedSecrets == null || _storedSecrets.Count != Secrets.Count || Secrets.Any(secret => _storedSecrets.GetValueOrDefault(secret.Key) != secret.Value);

        /// <summary>Called by whatever stored <see cref="Secrets"/>.</summary>
        internal void SecretsAreStored() => _storedSecrets = new Dictionary<string, string>(Secrets);

        /// <summary>Drops plain values stored for what updaemon sets itself, from before <paramref name="manifest"/> declared it.</summary>
        public void DropSettingsSetByUpdaemon(AppManifest manifest) => DropSetByUpdaemon(manifest, Settings);

        /// <summary>Drops the secret ones.</summary>
        public void DropSecretsSetByUpdaemon(AppManifest manifest) => DropSetByUpdaemon(manifest, Secrets);

        private static void DropSetByUpdaemon(AppManifest manifest, Dictionary<string, string> values)
        {
            foreach (string key in values.Keys.Where(manifest.IsSetByUpdaemon).ToList())
            {
                values.Remove(key);
            }
        }

        /// <summary>Moves plain values that <paramref name="manifest"/> declares secret into the secrets.</summary>
        public void ConformTo(AppManifest manifest)
        {
            foreach (string key in Settings.Keys.Where(manifest.IsSecret).ToList())
            {
                Secrets[key] = Settings[key];
                Settings.Remove(key);
            }
        }

        private static void EnsureNotSetByUpdaemon(AppManifest manifest, string key)
        {
            if (manifest.IsSetByUpdaemon(key))
            {
                throw new InvalidOperationException(AppManifest.SetByUpdaemon(key));
            }
        }

        /// <summary>Required settings of <paramref name="manifest"/> that have no value, in name order.</summary>
        public IReadOnlyList<string> MissingRequired(AppManifest manifest) =>
            manifest.MissingRequiredSettings(Settings.Keys.Concat(Secrets.Keys));

        /// <summary>
        /// Throws when <paramref name="version"/> needs a setting that has no value, before anything
        /// changes. The message names the 'updaemon set' command, then <paramref name="retryCommand"/>.
        /// </summary>
        public void EnsureComplete(AppManifest manifest, Version version, Version running, string retryCommand)
        {
            IReadOnlyList<string> missing = MissingRequired(manifest);
            if (missing.Count > 0)
            {
                string assignments = string.Join(' ', missing.Select(key => $"{key}=<value>"));
                throw new InvalidOperationException(
                    $"{version} needs {string.Join(", ", missing)}; {running} keeps running. Run 'updaemon set {App.Name} {assignments}', then '{retryCommand}'.");
            }
        }
    }
}
