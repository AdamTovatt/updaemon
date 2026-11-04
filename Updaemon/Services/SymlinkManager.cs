using Updaemon.Interfaces;
using Updaemon.IO;

namespace Updaemon.Services
{
    /// <summary>
    /// Manages symbolic links for service executables.
    /// </summary>
    public class SymlinkManager : ISymlinkManager
    {
        /// <summary>
        /// Points <paramref name="linkPath"/> at <paramref name="targetPath"/> in one step: a new link is
        /// made beside it and renamed over it, so the path never goes missing or points nowhere. A file
        /// at the path (an updaemon 0.x binary) is replaced the same way; a directory is refused.
        /// </summary>
        public Task CreateOrUpdateSymlinkAsync(string linkPath, string targetPath, CancellationToken cancellationToken = default)
        {
            if (Directory.Exists(linkPath) && File.ResolveLinkTarget(linkPath, returnFinalTarget: false) == null)
            {
                throw new InvalidOperationException($"Path '{linkPath}' is a directory, not a link.");
            }

            string temporaryLink = $"{linkPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.CreateSymbolicLink(temporaryLink, targetPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw CouldNotPoint(linkPath, targetPath, ex);
            }

            // rename(2) itself: File.Move refuses a link to a directory, such as an app's 'current'.
            try
            {
                LibC.Rename(temporaryLink, linkPath);
            }
            catch (IOException ex)
            {
                File.Delete(temporaryLink);
                throw CouldNotPoint(linkPath, targetPath, ex);
            }

            return Task.CompletedTask;
        }

        private static IOException CouldNotPoint(string linkPath, string targetPath, Exception ex) =>
            new IOException($"Could not point '{linkPath}' at '{targetPath}': {ex.Message}", ex);

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
    }
}

