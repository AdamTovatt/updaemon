using Updaemon.IO;

namespace Updaemon.Configuration
{
    /// <summary>
    /// The file this updaemon process runs from. Self-update replaces only the scope's own updaemon,
    /// so it compares the two.
    /// </summary>
    public sealed record RunningExecutable(string Path)
    {
        public static RunningExecutable Current { get; } = new RunningExecutable(
            Environment.ProcessPath ?? throw new InvalidOperationException("Cannot tell which file this updaemon runs from."));

        /// <summary>
        /// Whether this is the file <paramref name="commandPath"/> leads to. Links are followed all the
        /// way, in parent directories too, as the running file's own path already is.
        /// </summary>
        public bool IsReachedThrough(string commandPath)
        {
            string? command = LibC.RealPath(commandPath);
            return command != null && command == LibC.RealPath(Path);
        }
    }
}
