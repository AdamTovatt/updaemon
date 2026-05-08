using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Sets POSIX file permissions via <see cref="File.SetUnixFileMode(string, UnixFileMode)"/>.
    /// Works on both Linux and macOS without shelling out to chmod.
    /// </summary>
    public class FilePermissionManager : IFilePermissionManager
    {
        // 0755 — owner rwx, everyone else rx. Used for executables.
        private const UnixFileMode ExecutableMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        // Bits to OR into existing permissions for the recursive "make readable" pass.
        // Mirrors `chmod -R a+rX`: read for everyone, plus execute for everyone on directories
        // (the execute bit means "may enter" on a directory; X is the conditional form that
        // also applies execute if some execute bit is already set on the file).
        private const UnixFileMode ReadAddBits =
            UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

        private const UnixFileMode ExecAddBits =
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

        private readonly IOutputWriter _outputWriter;

        public FilePermissionManager(IOutputWriter outputWriter)
        {
            _outputWriter = outputWriter;
        }

        public Task SetExecutablePermissionsAsync(string executablePath, CancellationToken cancellationToken = default)
        {
            try
            {
                File.SetUnixFileMode(executablePath, ExecutableMode);
                _outputWriter.WriteLine($"Set executable permissions on: {executablePath}");
            }
            catch (Exception ex)
            {
                _outputWriter.WriteLine($"Warning: Could not set executable permissions on {executablePath}: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public Task SetDirectoryPermissionsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            try
            {
                ApplyAdditiveRead(directoryPath);
                _outputWriter.WriteLine($"Set directory permissions on: {directoryPath}");
            }
            catch (Exception ex)
            {
                _outputWriter.WriteLine($"Warning: Could not set directory permissions on {directoryPath}: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Iterative `chmod -R a+rX` equivalent. For directories: adds a+rx (so they're traversable).
        /// For files: adds a+r, and additionally a+x only if some execute bit is already set.
        /// Existing bits are preserved. Symlinks are not followed (avoids cycles and accidental
        /// permission changes outside the target tree).
        /// </summary>
        private static void ApplyAdditiveRead(string root)
        {
            // EnumerateFileSystemEntries with ReparsePoint skipped means we never descend into
            // a symlinked directory, which prevents cycles entirely.
            EnumerationOptions options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
            };

            // Apply to the root itself first.
            ApplyOne(root);

            foreach (string entry in Directory.EnumerateFileSystemEntries(root, "*", options))
            {
                ApplyOne(entry);
            }
        }

        private static void ApplyOne(string path)
        {
            FileAttributes attrs;
            try
            {
                attrs = File.GetAttributes(path);
            }
            catch
            {
                return;
            }

            // Don't try to chmod a symlink target via the symlink path — mirrors `chmod -P`.
            if (attrs.HasFlag(FileAttributes.ReparsePoint))
            {
                return;
            }

            UnixFileMode current;
            try
            {
                current = File.GetUnixFileMode(path);
            }
            catch
            {
                return;
            }

            UnixFileMode target = current | ReadAddBits;
            bool isDirectory = attrs.HasFlag(FileAttributes.Directory);
            bool anyExec = (current & ExecAddBits) != 0;
            if (isDirectory || anyExec)
            {
                target |= ExecAddBits;
            }

            if (target != current)
            {
                try
                {
                    File.SetUnixFileMode(path, target);
                }
                catch
                {
                    // Best effort — caller already logged a warning at the directory level.
                }
            }
        }
    }
}
