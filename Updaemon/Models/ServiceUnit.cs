namespace Updaemon.Models
{
    /// <summary>
    /// What a generated systemd unit or launchd plist runs. It holds no settings: the wrapper it
    /// starts loads them, and the secrets, when the service starts.
    /// </summary>
    public sealed record ServiceUnit
    {
        public required string AppName { get; init; }

        /// <summary>The app's <c>current</c> link, so the unit follows each update.</summary>
        public required string WorkingDirectory { get; init; }

        /// <summary>The app's service wrapper, which loads its settings and secrets and then runs the executable.</summary>
        public required string WrapperPath { get; init; }

        public required IReadOnlyList<string> Arguments { get; init; }

        /// <summary>The user the service runs as; null runs it as the scope's owner (root in the system scope).</summary>
        public string? RunAs { get; init; }
    }
}
