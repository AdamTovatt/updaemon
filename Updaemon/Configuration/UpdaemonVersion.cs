namespace Updaemon.Configuration
{
    /// <summary>
    /// The running updaemon's version. Its first three parts name its GitHub release
    /// (v&lt;major&gt;.&lt;minor&gt;.&lt;build&gt;), which first-party plugins are installed from.
    /// </summary>
    public sealed record UpdaemonVersion(Version Value)
    {
        // The assembly version, not the informational one, which the SDK may suffix with a commit hash.
        public static UpdaemonVersion Current { get; } = new UpdaemonVersion(typeof(UpdaemonVersion).Assembly.GetName().Version!);
    }
}
