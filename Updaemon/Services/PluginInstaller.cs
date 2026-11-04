using System.Net;
using Updaemon.Common.Models;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Installs distribution plugins. The new binary is downloaded into the plugins directory,
    /// verified against its release's checksum unless it comes from a plain URL, smoke-run by
    /// reading its service information (which also refuses another plugin
    /// protocol version), and only then renamed over the installed one, so a failure at any
    /// step leaves the installed plugin untouched.
    /// </summary>
    public class PluginInstaller : IPluginInstaller
    {
        private readonly IPluginUrlResolver _pluginUrlResolver;
        private readonly IGitHubPluginReleases _gitHubPluginReleases;
        private readonly IPluginDownloader _pluginDownloader;
        private readonly IConfigManager _configManager;
        private readonly string _pluginsDirectory;

        public PluginInstaller(
            IPluginUrlResolver pluginUrlResolver,
            IGitHubPluginReleases gitHubPluginReleases,
            IPluginDownloader pluginDownloader,
            IConfigManager configManager,
            UpdaemonScope scope)
        {
            _pluginUrlResolver = pluginUrlResolver;
            _gitHubPluginReleases = gitHubPluginReleases;
            _pluginDownloader = pluginDownloader;
            _configManager = configManager;
            _pluginsDirectory = scope.PluginsDirectory;
        }

        public async Task<PluginInstallResult> InstallAsync(PluginSource source, string? alias, CancellationToken cancellationToken = default)
        {
            PluginDownload pluginDownload = await ResolveDownloadAsync(source, cancellationToken);

            PluginDownloadResult download;
            try
            {
                download = await _pluginDownloader.DownloadAndInspectAsync(pluginDownload, _pluginsDirectory, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound && source is PluginSource.Registry registry)
            {
                throw new InvalidOperationException(
                    $"Updaemon's release has no '{registry.Name}' plugin built for this machine: {pluginDownload} was not found. " +
                    "Build the plugin for this machine and install it with 'updaemon dist-install <url>'.",
                    ex);
            }

            try
            {
                DistributionServiceInformation serviceInformation = download.ServiceInformation;
                string finalAlias = alias ?? (source is PluginSource.Registry registry ? registry.Name : serviceInformation.DefaultAlias);
                if (string.IsNullOrWhiteSpace(finalAlias))
                {
                    throw new InvalidOperationException("The plugin has no default alias. Give one with --as <alias>.");
                }

                InstalledPluginInfo? existing = await _configManager.GetPluginAsync(finalAlias, cancellationToken);

                string pluginDirectory = Path.Combine(_pluginsDirectory, finalAlias);
                Directory.CreateDirectory(pluginDirectory);
                string pluginPath = Path.Combine(pluginDirectory, pluginDownload.FileName);

                // A rename within one filesystem: the old binary is replaced in one step.
                File.Move(download.TempFilePath, pluginPath, overwrite: true);

                InstalledPluginInfo plugin = new InstalledPluginInfo
                {
                    Alias = finalAlias,
                    Path = pluginPath,
                    Source = source,
                };
                await _configManager.AddOrUpdatePluginAsync(plugin, cancellationToken);

                // Only once the config points at the new binary, so a failed save leaves the old one usable.
                if (existing != null && existing.Path != pluginPath)
                {
                    TryDelete(existing.Path);
                }

                return new PluginInstallResult
                {
                    Plugin = plugin,
                    ServiceInformation = serviceInformation,
                    ReplacedExisting = existing != null,
                };
            }
            finally
            {
                TryDelete(download.TempFilePath);
            }
        }

        private async Task<PluginDownload> ResolveDownloadAsync(PluginSource source, CancellationToken cancellationToken)
        {
            switch (source)
            {
                case PluginSource.Url url:
                    Uri address = new Uri(url.Address);
                    return new PluginDownload(address, FileNameOf(address), Checksums: null, GitHub: null);

                case PluginSource.GitHub github:
                    return await _gitHubPluginReleases.GetBuildAsync(github.Owner, github.Repo, github.Tag, cancellationToken);

                case PluginSource.Registry registry:
                    string? resolved = _pluginUrlResolver.Resolve(registry.Name, registry.Release);
                    if (resolved == null)
                    {
                        throw new InvalidOperationException(
                            $"'{registry.Name}' is not in the plugin registry ({string.Join(", ", _pluginUrlResolver.PluginNames)}). " +
                            "Install it from its URL with 'updaemon dist-install <url>'.");
                    }

                    Uri build = new Uri(resolved);
                    return new PluginDownload(build, FileNameOf(build), ReleaseChecksums.BesideAsset(build), GitHub: null);

                default:
                    throw new InvalidOperationException($"Unknown plugin source: {source}");
            }
        }

        private static string FileNameOf(Uri address)
        {
            string fileName = Path.GetFileName(address.LocalPath);
            return string.IsNullOrEmpty(fileName) ? "distribution-plugin" : fileName;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort: a leftover file does not affect the installed plugin.
            }
        }
    }
}
