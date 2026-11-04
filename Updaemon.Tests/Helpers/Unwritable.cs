using Updaemon.IO;

namespace Updaemon.Tests.Helpers
{
    /// <summary>
    /// Makes a directory unwritable for real, as a full disk or a wrong owner would. Root writes to a
    /// directory of any mode, so tests using this skip under root with <see cref="SkipUnderRoot"/>.
    /// </summary>
    public static class Unwritable
    {
        private const UnixFileMode ReadOnlyDirectory = UnixFileMode.UserRead | UnixFileMode.UserExecute;

        public static void SkipUnderRoot()
        {
            Skip.If(LibC.EffectiveUserId() == 0, "Root writes to a directory of any mode.");
        }

        /// <summary>Makes <paramref name="directory"/> unwritable until the returned scope is disposed.</summary>
        public static IDisposable Make(string directory)
        {
            UnixFileMode mode = File.GetUnixFileMode(directory);
            File.SetUnixFileMode(directory, ReadOnlyDirectory);
            return new Restore(directory, mode);
        }

        /// <summary>Runs <paramref name="action"/> with <paramref name="directory"/> unwritable, and makes it writable again after.</summary>
        public static async Task<T> WhileAsync<T>(string directory, Func<Task<T>> action)
        {
            using (Make(directory))
            {
                return await action();
            }
        }

        private sealed class Restore : IDisposable
        {
            private readonly string _directory;
            private readonly UnixFileMode _mode;

            public Restore(string directory, UnixFileMode mode)
            {
                _directory = directory;
                _mode = mode;
            }

            public void Dispose()
            {
                File.SetUnixFileMode(_directory, _mode);
            }
        }
    }
}
