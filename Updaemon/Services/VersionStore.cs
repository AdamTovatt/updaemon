using Updaemon.Common.Models;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.IO;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// The versions of apps and extensions on disk, each package by its name: downloads them, points the link
    /// to the current one and prunes old ones. What is in a version directory is complete: a download becomes one only when done.
    /// </summary>
    public class VersionStore
    {
        /// <summary>What a download's directory is called until it is complete: not a version.</summary>
        private const string PartialSuffix = ".partial";

        private readonly UpdaemonScope _scope;
        private readonly ISymlinkManager _symlinkManager;
        private readonly IFilePermissionManager _filePermissionManager;
        private readonly IOutputWriter _outputWriter;

        public VersionStore(UpdaemonScope scope, ISymlinkManager symlinkManager, IFilePermissionManager filePermissionManager, IOutputWriter outputWriter)
        {
            _scope = scope;
            _symlinkManager = symlinkManager;
            _filePermissionManager = filePermissionManager;
            _outputWriter = outputWriter;
        }

        /// <summary>The version the package's link points at.</summary>
        /// <exception cref="InvalidOperationException">There is no current version; the message says how to recover.</exception>
        public async Task<Version> GetCurrentAsync(PackageName name, CancellationToken cancellationToken = default)
        {
            return await FindCurrentAsync(name, cancellationToken) ?? throw NotDeployed(name);
        }

        /// <summary>The version the package's link points at, or null.</summary>
        public async Task<Version?> FindCurrentAsync(PackageName name, CancellationToken cancellationToken = default)
        {
            string? target = await _symlinkManager.ReadSymlinkAsync(_scope.Locate(name).CurrentLink, cancellationToken);
            return target != null && Version.TryParse(Path.GetFileName(target), out Version? version) ? version : null;
        }

        /// <summary>The versions kept, the current one included, oldest first.</summary>
        public IReadOnlyList<Version> GetRetained(PackageName name)
        {
            string directory = _scope.Locate(name).VersionsDirectory;
            return !Directory.Exists(directory)
                ? Array.Empty<Version>()
                : Directory.GetDirectories(directory)
                    .Select(path => Version.TryParse(Path.GetFileName(path), out Version? version) ? version : null)
                    .OfType<Version>()
                    .Order()
                    .ToList();
        }

        /// <summary>A kept version's directory.</summary>
        /// <exception cref="InvalidOperationException">The version is not kept; the message lists the kept ones.</exception>
        public string OpenRetained(PackageName name, Version version)
        {
            string directory = GetVersionDirectory(name, version);
            return Directory.Exists(directory) ? directory : throw NotKept(name, version);
        }

        public InvalidOperationException NotKept(PackageName name, Version version)
        {
            IReadOnlyList<Version> kept = GetRetained(name);
            return new InvalidOperationException($"{name} {version} is not kept. Kept versions: {(kept.Count == 0 ? "none" : string.Join(", ", kept))}.");
        }

        /// <summary>
        /// Downloads a version, has <paramref name="read"/> make of it what the caller needs and makes it
        /// readable. It becomes a version directory only once all that is done; nothing is left if anything fails.
        /// </summary>
        /// <param name="read">Given where the complete download is, which is not yet its version directory.</param>
        public async Task<T> DownloadAsync<T>(
            PackageName name, InstalledPackage package, Version version, IDistributionServiceClient distributionClient, Func<string, Task<T>> read, CancellationToken cancellationToken = default)
        {
            if (version == await FindCurrentAsync(name, cancellationToken))
            {
                throw new InvalidOperationException($"{name} {version} is already the current version.");
            }

            PackageLocation location = _scope.Locate(name);
            string versionDirectory = GetVersionDirectory(name, version);
            string partialDirectory = versionDirectory + PartialSuffix;
            try
            {
                _outputWriter.WriteLine($"Downloading {name} {version}...");
                DeletePartialDownloads(location);
                Directory.CreateDirectory(partialDirectory);
                DownloadResult download = await distributionClient.DownloadVersionAsync(package.Remote, version, partialDirectory, cancellationToken);
                if (!download.Verified)
                {
                    _outputWriter.WriteLine($"Not verified: {name} {version} came without a checksum to check it against.");
                }

                T content = await read(partialDirectory);
                await _filePermissionManager.MakeTreeReadableAsync(partialDirectory, cancellationToken);
                await MakeEnterableAsync(location, cancellationToken);

                // Left over from an attempt that was not activated.
                Files.DeleteDirectoryIfExists(versionDirectory);
                Directory.Move(partialDirectory, versionDirectory);
                return content;
            }
            catch
            {
                TryDelete(partialDirectory);
                DeleteEmptyDirectories(location);
                throw;
            }
        }

        /// <summary>Downloads a version of which the caller needs nothing more.</summary>
        /// <returns>The version.</returns>
        public Task<Version> DownloadAsync(
            PackageName name, InstalledPackage package, Version version, IDistributionServiceClient distributionClient, CancellationToken cancellationToken = default) =>
            DownloadAsync(name, package, version, distributionClient, _ => Task.FromResult(version), cancellationToken);

        /// <summary>
        /// Lets everyone read every version of the app, whoever put them there, as <see cref="DownloadAsync"/>
        /// does for the one it downloads: whoever runs the app, or loads the extension, need not be who installed it.
        /// </summary>
        public Task MakeReadableAsync(AppName name, CancellationToken cancellationToken = default) =>
            _filePermissionManager.MakeTreeReadableAsync(_scope.Locate(name).AppDirectory, cancellationToken);

        /// <summary>Lets everyone reach the versions, each of which has permissions of its own.</summary>
        private async Task MakeEnterableAsync(PackageLocation location, CancellationToken cancellationToken)
        {
            foreach (string directory in GetDirectories(location))
            {
                await _filePermissionManager.MakeDirectoryEnterableAsync(directory, cancellationToken);
            }
        }

        /// <summary>Where the versions are and what that is in, up to the app's own directory.</summary>
        private static IEnumerable<string> GetDirectories(PackageLocation location)
        {
            for (string? directory = location.VersionsDirectory;
                directory != null && directory.StartsWith(location.AppDirectory, StringComparison.Ordinal);
                directory = Path.GetDirectoryName(directory))
            {
                yield return directory;
            }
        }

        /// <summary>Makes a kept version current.</summary>
        public Task PointAsync(PackageName name, Version version, CancellationToken cancellationToken = default)
        {
            PackageLocation location = _scope.Locate(name);
            // The new link is made among the versions, not beside the old one: a host loads whatever is in its folder of extensions.
            return _symlinkManager.CreateOrUpdateSymlinkAsync(location.CurrentLink, OpenRetained(name, version), location.VersionsDirectory, cancellationToken);
        }

        /// <summary>Removes the link to the current version, if there is one.</summary>
        public void Unlink(PackageName name)
        {
            string link = _scope.Locate(name).CurrentLink;
            if (new FileInfo(link).LinkTarget != null)
            {
                File.Delete(link);
            }
        }

        /// <summary>Deletes every version of a package that is not linked any more.</summary>
        public void DeleteAll(PackageName name)
        {
            PackageLocation location = _scope.Locate(name);
            Files.DeleteDirectoryIfExists(location.VersionsDirectory);
            DeleteEmptyDirectories(location);
        }

        /// <summary>The same, where what it fails to delete may stay: it is not linked, and goes with the host or when it is installed again.</summary>
        public void TryDeleteAll(PackageName name)
        {
            try
            {
                DeleteAll(name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _outputWriter.WriteError($"Warning: could not delete the versions of {name}: {ex.Message}");
            }
        }

        /// <summary>Deletes a downloaded version that will not be activated, and the directories that leaves empty.</summary>
        public async Task DiscardAsync(PackageName name, Version version)
        {
            // An activation whose switch back failed leaves it current: it is then all there is.
            if (version == await FindCurrentAsync(name))
            {
                return;
            }

            TryDelete(GetVersionDirectory(name, version));
            DeleteEmptyDirectories(_scope.Locate(name));
        }

        /// <summary>Deletes the oldest versions beyond <paramref name="retentionCount"/>, never the current one.</summary>
        public async Task PruneAsync(PackageName name, int retentionCount, CancellationToken cancellationToken = default)
        {
            Version? current = await FindCurrentAsync(name, cancellationToken);
            foreach (Version version in GetRetained(name).Reverse().Skip(Math.Max(retentionCount, 1)).Where(version => version != current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Directory.Delete(GetVersionDirectory(name, version), recursive: true);
                }
                catch (Exception ex)
                {
                    _outputWriter.WriteError($"Warning: Failed to prune {version}: {ex.Message}");
                }
            }
        }

        public string GetVersionDirectory(PackageName name, Version version) => Path.Combine(_scope.Locate(name).VersionsDirectory, version.ToString());

        public static InvalidOperationException NotDeployed(PackageName name) =>
            new InvalidOperationException($"{name} has no current version. Remove it with 'updaemon remove {name}', then install it again.");

        /// <summary>What a killed run left of its downloads.</summary>
        private static void DeletePartialDownloads(PackageLocation location)
        {
            if (Directory.Exists(location.VersionsDirectory))
            {
                foreach (string partial in Directory.GetDirectories(location.VersionsDirectory, "*" + PartialSuffix))
                {
                    Directory.Delete(partial, recursive: true);
                }
            }
        }

        /// <summary>A first install's version was all there was: leave no empty directory behind, up to the app's own. Best effort.</summary>
        private static void DeleteEmptyDirectories(PackageLocation location)
        {
            try
            {
                foreach (string directory in GetDirectories(location))
                {
                    if (Directory.Exists(directory))
                    {
                        if (Directory.EnumerateFileSystemEntries(directory).Any())
                        {
                            return;
                        }

                        Directory.Delete(directory);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort, as for the version itself.
            }
        }

        /// <summary>For cleaning up a version that was never activated: a leftover is replaced on the next attempt.</summary>
        private static void TryDelete(string directory)
        {
            try
            {
                Files.DeleteDirectoryIfExists(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort, see above.
            }
        }
    }
}
