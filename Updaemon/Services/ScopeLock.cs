using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// An exclusive OS file lock on the scope root's lock file. On Unix, .NET takes it with
    /// <c>flock</c> when the file is opened with <see cref="FileShare.None"/>, so the OS releases
    /// it when the holding process exits, however it exits: there are no stale locks.
    /// </summary>
    public class ScopeLock : IRunLock
    {
        private readonly UpdaemonScope _scope;
        private readonly IOutputWriter _outputWriter;
        private readonly TimeSpan _retryInterval;

        public ScopeLock(UpdaemonScope scope, IOutputWriter outputWriter)
            : this(scope, outputWriter, TimeSpan.FromSeconds(1))
        {
        }

        public ScopeLock(UpdaemonScope scope, IOutputWriter outputWriter, TimeSpan retryInterval)
        {
            _scope = scope;
            _outputWriter = outputWriter;
            _retryInterval = retryInterval;
        }

        public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            foreach (string directory in _scope.WritableDirectories)
            {
                EnsureWritable(directory);
            }

            bool announced = false;
            while (true)
            {
                try
                {
                    return new FileStream(_scope.LockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    if (!announced)
                    {
                        _outputWriter.WriteLine("Another updaemon run is in progress; waiting for it to finish...");
                        announced = true;
                    }

                    await Task.Delay(_retryInterval, cancellationToken);
                }
            }
        }

        private static void EnsureWritable(string directory)
        {
            string probePath = Path.Combine(directory, $".updaemon-write-check-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(probePath, string.Empty);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                throw new InvalidOperationException($"Cannot write to '{directory}': {ex.Message} Check that this user owns it.", ex);
            }
            finally
            {
                try { File.Delete(probePath); } catch { /* best-effort cleanup of the probe file */ }
            }
        }
    }
}
