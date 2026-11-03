using Updaemon.Common.Utilities;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Updates GitHub-source plugins at the end of <c>update</c>, while self-update is on, beside
    /// updaemon's own update. A plugin whose repository's latest release differs from the one installed
    /// gets it through the installer's verify, smoke-run and swap, so a release that fails any step
    /// leaves the installed one. A release refused for good is recorded as skipped and passed over
    /// while it is the latest, as an app's skipped versions are; any other failure is retried next time.
    /// </summary>
    public class GitHubPluginUpdater
    {
        private readonly IConfigManager _configManager;
        private readonly IGitHubPluginReleases _releases;
        private readonly IPluginInstaller _pluginInstaller;
        private readonly IOutputWriter _outputWriter;

        public GitHubPluginUpdater(IConfigManager configManager, IGitHubPluginReleases releases, IPluginInstaller pluginInstaller, IOutputWriter outputWriter)
        {
            _configManager = configManager;
            _releases = releases;
            _pluginInstaller = pluginInstaller;
            _outputWriter = outputWriter;
        }

        /// <summary>Returns false when a plugin could not be checked or updated; each failure is reported, and the others still run.</summary>
        public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            UpdaemonConfig config = await _configManager.LoadConfigAsync(cancellationToken);
            if (!config.SelfUpdate)
            {
                return true;
            }

            bool allSucceeded = true;
            foreach (InstalledPluginInfo plugin in config.InstalledPlugins.Values)
            {
                if (plugin.Source is PluginSource.GitHub installed)
                {
                    allSucceeded &= await UpdateAsync(plugin, installed, cancellationToken);
                }
            }

            return allSucceeded;
        }

        /// <summary>
        /// A refusal that trying again cannot change: the release does not match its SHA256SUMS (or has
        /// none), has no single build for this machine, or speaks another plugin protocol.
        /// </summary>
        private static bool RefusesTheRelease(Exception ex) => ex is ChecksumException or ReleaseBuildException or PluginProtocolException;

        private async Task<bool> UpdateAsync(InstalledPluginInfo plugin, PluginSource.GitHub installed, CancellationToken cancellationToken)
        {
            string alias = plugin.Alias;
            string? latestTag = null;
            try
            {
                // The latest release is the one GitHub marks Latest; any other tag than the installed one replaces it.
                latestTag = await _releases.GetLatestTagAsync(installed.Owner, installed.Repo, cancellationToken);
                if (latestTag == installed.Tag)
                {
                    _outputWriter.WriteLine($"The '{alias}' plugin is up to date ({installed.Tag}).");
                    return true;
                }

                if (latestTag == installed.SkippedTag)
                {
                    _outputWriter.WriteLine($"The '{alias}' plugin skips {latestTag}, which was refused; {installed.Tag} stays.");
                    return true;
                }

                await _pluginInstaller.InstallAsync(installed with { Tag = latestTag, SkippedTag = null }, alias, cancellationToken);
                _outputWriter.WriteLine($"Updated the '{alias}' plugin from {installed.Tag} to {latestTag}.");
                return true;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                string attempt = latestTag == null ? "check for a newer release" : $"update to {latestTag}";
                string skip = string.Empty;
                if (latestTag != null && RefusesTheRelease(ex))
                {
                    skip = await TrySkipAsync(plugin, installed, latestTag, cancellationToken);
                }

                _outputWriter.WriteError($"Error: the '{alias}' plugin ({installed}) could not {attempt}: {ex.Message.TrimEnd('.')}. {installed.Tag} stays.{skip}");
                return false;
            }
        }

        /// <summary>Records <paramref name="refusedTag"/> as skipped; returns the sentence that says so, or why it could not.</summary>
        private async Task<string> TrySkipAsync(InstalledPluginInfo plugin, PluginSource.GitHub installed, string refusedTag, CancellationToken cancellationToken)
        {
            try
            {
                await _configManager.AddOrUpdatePluginAsync(
                    new InstalledPluginInfo { Alias = plugin.Alias, Path = plugin.Path, Source = installed with { SkippedTag = refusedTag } },
                    cancellationToken);
                return $" Later updates skip {refusedTag} until another release is latest.";
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return $" Recording that {refusedTag} is skipped failed: {ex.Message.TrimEnd('.')}.";
            }
        }
    }
}
