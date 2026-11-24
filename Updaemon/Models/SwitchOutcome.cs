namespace Updaemon.Models
{
    /// <summary>How a switch ended that did not fail otherwise. <see cref="Services.SwitchText"/> words it.</summary>
    /// <param name="Switched">What it switched, the app itself first.</param>
    /// <param name="Restart">Whether the service was to be restarted.</param>
    public abstract record SwitchOutcome(IReadOnlyList<PackageSwitch> Switched, RestartDecision Restart)
    {
        /// <summary>It is current, and the service, if it was restarted, stays up.</summary>
        public sealed record Stands(IReadOnlyList<PackageSwitch> Switched, RestartDecision Restart) : SwitchOutcome(Switched, Restart);

        /// <summary>It is current, as it stands whatever the service does, but the service is not up.</summary>
        /// <param name="RestartFailure">Why it could not be restarted; null when it was and did not stay up.</param>
        public sealed record StandsDown(IReadOnlyList<PackageSwitch> Switched, RestartDecision Restart, string? RestartFailure) : SwitchOutcome(Switched, Restart);

        /// <summary>The service did not stay up with it; what was current before is again.</summary>
        /// <param name="StaysUp">Whether the service stays up on that; null when that is no service.</param>
        /// <param name="SkipNotRecorded">Why the versions switched to could not be recorded as skipped; null when they were.</param>
        public sealed record WentBack(IReadOnlyList<PackageSwitch> Switched, RestartDecision Restart, bool? StaysUp, string? SkipNotRecorded) : SwitchOutcome(Switched, Restart);

        /// <summary>The service did not stay up with it, and going back failed too.</summary>
        public sealed record NotBack(IReadOnlyList<PackageSwitch> Switched, RestartDecision Restart, string Problem) : SwitchOutcome(Switched, Restart);
    }

    /// <summary>Whether a switch restarts the service, or why not.</summary>
    public enum RestartDecision
    {
        Restart,
        NoService,

        /// <summary>The app is set to manual restart.</summary>
        Manual,

        /// <summary>The service is disabled, and only extensions are switched.</summary>
        Disabled,
    }
}
