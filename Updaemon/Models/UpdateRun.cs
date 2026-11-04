using System.Text.Json.Serialization;

namespace Updaemon.Models
{
    /// <summary>What started an <c>update</c> run.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<UpdateStarter>))]
    public enum UpdateStarter
    {
        Person,
        Timer,
    }

    /// <summary>
    /// How the update timer's unit or plist runs updaemon: what the timer managers write and
    /// <c>update</c> reads, so the run is recorded as <see cref="UpdateStarter.Timer"/>'s.
    /// </summary>
    public static class TimerRun
    {
        public const string Option = "--timer";

        public static IReadOnlyList<string> Arguments { get; } = new[] { "update", Option };
    }

    /// <summary>One thing an update run could not update: an app, a plugin or updaemon itself, and why.</summary>
    public sealed record UpdateFailure(string Subject, string Reason);

    /// <summary>The last <c>update</c> run: when it finished, what started it, and what failed.</summary>
    public sealed record UpdateRun(DateTimeOffset FinishedAt, UpdateStarter StartedBy, IReadOnlyList<UpdateFailure> Failures)
    {
        /// <summary>This run without the failures of <paramref name="subjects"/>, once they were updated since.</summary>
        public UpdateRun WithoutFailuresOf(IEnumerable<string> subjects)
        {
            HashSet<string> fixedSubjects = subjects.ToHashSet();
            return this with { Failures = Failures.Where(failure => !fixedSubjects.Contains(failure.Subject)).ToList() };
        }
    }
}
