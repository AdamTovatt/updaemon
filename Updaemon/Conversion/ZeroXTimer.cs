namespace Updaemon.Conversion
{
    /// <summary>
    /// Updaemon 0.x's update timer where it is not this updaemon's own: on macOS, a launchd job under
    /// 0.x's label. Its plist, whether it was on, and its interval when it can be read.
    /// </summary>
    public sealed record ZeroXTimer(string FilePath, bool IsOn, TimeSpan? Interval);
}
