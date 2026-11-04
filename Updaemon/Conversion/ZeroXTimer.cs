namespace Updaemon.Conversion
{
    /// <summary>
    /// Updaemon 0.x's update timer where it is not this updaemon's own: on macOS, a launchd job under
    /// 0.x's label; on Linux, units 0.x's 'timer -' left behind, disabled, or units that are on but run
    /// 'update' as 0.x wrote it. The files conversion replaces or removes (each kept in the backup),
    /// whether the timer was on, and its interval when it can be read.
    /// </summary>
    public sealed record ZeroXTimer(IReadOnlyList<string> Files, bool IsOn, TimeSpan? Interval);
}
