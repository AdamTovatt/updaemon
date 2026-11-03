namespace Updaemon.Models
{
    /// <summary>
    /// What a generated systemd unit or launchd plist runs, and with which environment.
    /// </summary>
    public sealed record ServiceUnit
    {
        public required string AppName { get; init; }

        /// <summary>The app's <c>current</c> link, so the unit follows each update.</summary>
        public required string WorkingDirectory { get; init; }

        /// <summary>Absolute path to the executable, through the <c>current</c> link.</summary>
        public required string ExecutablePath { get; init; }

        public required IReadOnlyList<string> Arguments { get; init; }

        /// <summary>The user the service runs as; null runs it as the scope's owner (root in the system scope).</summary>
        public string? RunAs { get; init; }

        /// <summary>The app's settings file, loaded by systemd with <c>EnvironmentFile=</c>.</summary>
        public required string SettingsFilePath { get; init; }

        /// <summary>The app's secrets file, loaded by systemd with <c>EnvironmentFile=</c> when present.</summary>
        public required string SecretsFilePath { get; init; }

        /// <summary>Every variable the service gets, secrets included. launchd has no environment files, so the plist carries these.</summary>
        public required IReadOnlyDictionary<string, string> Environment { get; init; }
    }
}
