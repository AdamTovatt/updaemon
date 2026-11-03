using System.ComponentModel;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.IO;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Updates updaemon itself, at the end of <c>update</c>. The new version is downloaded beside the
    /// old one under a temporary name, smoke-run with <c>--version</c> and only then moved into its
    /// version directory; the scope's <c>updaemon</c> link is switched to it last, in one step.
    /// Registry plugins follow on their next use (see <see cref="PluginProvider"/>), since only the
    /// new updaemon can tell whether they suit it.
    /// </summary>
    public class SelfUpdater
    {
        private readonly UpdaemonScope _scope;
        private readonly IConfigManager _configManager;
        private readonly IUpdaemonReleases _releases;
        private readonly ISymlinkManager _symlinkManager;
        private readonly IOutputWriter _outputWriter;
        private readonly UpdaemonVersion _runningVersion;
        private readonly RunningExecutable _runningExecutable;

        public SelfUpdater(
            UpdaemonScope scope,
            IConfigManager configManager,
            IUpdaemonReleases releases,
            ISymlinkManager symlinkManager,
            IOutputWriter outputWriter,
            UpdaemonVersion runningVersion,
            RunningExecutable runningExecutable)
        {
            _scope = scope;
            _configManager = configManager;
            _releases = releases;
            _symlinkManager = symlinkManager;
            _outputWriter = outputWriter;
            _runningVersion = runningVersion;
            _runningExecutable = runningExecutable;
        }

        /// <summary>How long the new version's <c>--version</c> may take; a build that hangs is not installed.</summary>
        internal TimeSpan SmokeRunTimeout { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>Returns false when updating failed; the old version then stays and the reason is reported.</summary>
        public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await _configManager.LoadConfigAsync(cancellationToken);
            if (!config.SelfUpdate)
            {
                return true;
            }

            if (!_runningExecutable.IsReachedThrough(_scope.UpdaemonCommandPath))
            {
                _outputWriter.WriteLine(
                    $"Self-update skipped: it replaces {_scope.UpdaemonCommandPath}, and this updaemon is {_runningExecutable.Path}.");
                return true;
            }

            UpdaemonVersion current = _runningVersion;
            UpdaemonVersion? latest;
            try
            {
                latest = await _releases.GetLatestVersionAsync(cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                return Fail($"could not read updaemon's latest release: {ex.Message}");
            }

            if (latest == null)
            {
                return Fail("could not read updaemon's latest release: its tag is not v<x.y.z>");
            }

            if (latest <= current)
            {
                _outputWriter.WriteLine($"updaemon is up to date ({current}).");
                return true;
            }

            string binary = _scope.GetUpdaemonBinaryPath(latest);
            string? problem = await InstallAsync(latest, binary, cancellationToken);
            if (problem != null)
            {
                return Fail($"updating updaemon to {latest} failed: {problem}. {current} stays.");
            }

            try
            {
                Directory.CreateDirectory(_scope.BinDirectory);
                await _symlinkManager.CreateOrUpdateSymlinkAsync(_scope.UpdaemonCommandPath, binary, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail($"updating updaemon to {latest} failed: {ex.Message.TrimEnd('.')}. {current} stays.");
            }

            _outputWriter.WriteLine($"Updated updaemon from {current} to {latest}.");

            foreach (InstalledPluginInfo plugin in config.InstalledPlugins.Values.Where(plugin => !plugin.Source.FollowsUpdaemon))
            {
                _outputWriter.WriteLine(
                    $"The '{plugin.Alias}' plugin was installed from a URL, so it is not updated with updaemon. " +
                    $"If it needs a newer build, reinstall it with 'updaemon dist-install --as {plugin.Alias} <url>'.");
            }

            PruneVersions(keep: new[] { latest, current });
            return true;
        }

        /// <summary>
        /// Puts a smoke-run build of <paramref name="release"/> at <paramref name="binary"/>; returns
        /// what went wrong, or null. On failure nothing new is left behind.
        /// </summary>
        private async Task<string?> InstallAsync(UpdaemonVersion release, string binary, CancellationToken cancellationToken)
        {
            string directory = Path.GetDirectoryName(binary)!;
            string download = $"{binary}.{Guid.NewGuid():N}.download";
            try
            {
                CreateDirectoryOthersCanRun(_scope.UpdaemonVersionsDirectory);
                CreateDirectoryOthersCanRun(directory);
                string? problem = await DownloadAndSmokeRunAsync(release, download, cancellationToken);
                if (problem == null)
                {
                    File.Move(download, binary, overwrite: true);
                }

                return problem;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ex.Message.TrimEnd('.');
            }
            finally
            {
                TryDelete(() => File.Delete(download));
                TryDelete(() => Directory.Delete(directory, recursive: false));
            }
        }

        private async Task<string?> DownloadAndSmokeRunAsync(UpdaemonVersion release, string file, CancellationToken cancellationToken)
        {
            try
            {
                await _releases.DownloadAsync(release, file, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                return $"the download failed: {ex.Message.TrimEnd('.')}";
            }

            File.SetUnixFileMode(file, AtomicFile.Executable);
            ProcessRunner.Result smoke;
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(SmokeRunTimeout);
                try
                {
                    smoke = await ProcessRunner.RunAsync(file, new[] { "--version" }, timeout.Token);
                }
                catch (Win32Exception ex)
                {
                    return $"it does not run: {ex.Message.TrimEnd('.')}";
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return $"'updaemon --version' did not finish within {SmokeRunTimeout.TotalSeconds} seconds";
                }
            }

            string printed = smoke.StdOut.Trim();
            if (!smoke.Success || printed != release.ToString())
            {
                return $"'updaemon --version' exited {smoke.ExitCode} printing '{printed}' instead of '{release}'";
            }

            return null;
        }

        /// <summary>
        /// Creates the directory readable and searchable by everyone, whatever the umask: other users run
        /// the system scope's updaemon from it.
        /// </summary>
        private static void CreateDirectoryOthersCanRun(string directory)
        {
            Directory.CreateDirectory(directory);
            File.SetUnixFileMode(directory, AtomicFile.Executable);
        }

        /// <summary>Keeps the version now linked and the one that ran before it, for going back by hand.</summary>
        private void PruneVersions(IReadOnlyCollection<UpdaemonVersion> keep)
        {
            foreach (string directory in Directory.GetDirectories(_scope.UpdaemonVersionsDirectory))
            {
                if (!UpdaemonVersion.TryParse(Path.GetFileName(directory), out UpdaemonVersion? version) || !keep.Contains(version))
                {
                    TryDelete(() => Directory.Delete(directory, recursive: true));
                }
            }
        }

        private bool Fail(string message)
        {
            _outputWriter.WriteError($"Error: {message}");
            return false;
        }

        /// <summary>Best effort: a leftover is replaced or pruned on a later run.</summary>
        private static void TryDelete(Action delete)
        {
            try
            {
                delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Missing, not empty, or not ours to delete: leave it.
            }
        }
    }
}
