using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Manages symbolic links for service executables.
    /// </summary>
    public class SymlinkManager : ISymlinkManager
    {
        public Task CreateOrUpdateSymlinkAsync(string linkPath, string targetPath, CancellationToken cancellationToken = default)
        {
            // If symlink already exists, delete it first
            if (File.Exists(linkPath) || Directory.Exists(linkPath))
            {
                FileAttributes attributes = File.GetAttributes(linkPath);
                if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    File.Delete(linkPath);
                }
                else
                {
                    throw new InvalidOperationException($"Path '{linkPath}' exists but is not a symbolic link.");
                }
            }

            // Create the symlink
            File.CreateSymbolicLink(linkPath, targetPath);
            return Task.CompletedTask;
        }

        public Task<string?> ReadSymlinkAsync(string linkPath, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(linkPath) && !Directory.Exists(linkPath))
                {
                    return Task.FromResult<string?>(null);
                }

                FileAttributes attributes = File.GetAttributes(linkPath);
                if ((attributes & FileAttributes.ReparsePoint) != FileAttributes.ReparsePoint)
                {
                    return Task.FromResult<string?>(null);
                }

                FileSystemInfo info = File.ResolveLinkTarget(linkPath, returnFinalTarget: false)!;
                return Task.FromResult<string?>(info.FullName);
            }
            catch
            {
                return Task.FromResult<string?>(null);
            }
        }

        public Task<bool> IsSymlinkAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    return Task.FromResult(false);
                }

                FileAttributes attributes = File.GetAttributes(path);
                return Task.FromResult((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }
}

