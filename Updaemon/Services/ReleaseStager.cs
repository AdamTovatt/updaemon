using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>Downloads a package's latest release for <c>install</c>, with the plugin it comes from.</summary>
    public class ReleaseStager
    {
        private readonly IDistributionServiceClient _distributionClient;
        private readonly ISecretsManager _secretsManager;
        private readonly PluginProvider _pluginProvider;
        private readonly PluginCredentials _credentials;

        public ReleaseStager(IDistributionServiceClient distributionClient, ISecretsManager secretsManager, PluginProvider pluginProvider, PluginCredentials credentials)
        {
            _distributionClient = distributionClient;
            _secretsManager = secretsManager;
            _pluginProvider = pluginProvider;
            _credentials = credentials;
        }

        /// <summary>What <paramref name="stage"/> makes of the latest release.</summary>
        /// <exception cref="PluginException">There is no plugin to download with, or it failed.</exception>
        /// <exception cref="NoReleaseException">The source has no released version.</exception>
        /// <exception cref="Common.Utilities.CredentialNeededException">The plugin needs a secret.</exception>
        public async Task<T> StageLatestAsync<T>(PackageSource source, Func<Version, IDistributionServiceClient, Task<T>> stage, CancellationToken cancellationToken = default)
        {
            InstalledPluginInfo plugin = await _pluginProvider.GetOrInstallAsync(source.Plugin, cancellationToken);
            // Each try starts the plugin with its secrets as stored.
            return await _credentials.RunAsync(source.Plugin, () => StageAsync(source, plugin, stage, cancellationToken), handOverSavedSecret: null, cancellationToken);
        }

        private async Task<T> StageAsync<T>(PackageSource source, InstalledPluginInfo plugin, Func<Version, IDistributionServiceClient, Task<T>> stage, CancellationToken cancellationToken)
        {
            await _distributionClient.ConnectAsync(plugin.Path, cancellationToken);
            try
            {
                await _distributionClient.InitializeAsync(await _secretsManager.GetPluginSecretsFormattedAsync(source.Plugin, cancellationToken), cancellationToken);

                Version latest = await _distributionClient.GetLatestVersionAsync(source.Remote, cancellationToken)
                    ?? throw new NoReleaseException(source);
                return await stage(latest, _distributionClient);
            }
            finally
            {
                await _distributionClient.DisposeAsync();
            }
        }
    }

    /// <summary>A source without any released version.</summary>
    public sealed class NoReleaseException : Exception
    {
        public NoReleaseException(PackageSource source)
            : base($"{source} has no released version.")
        {
        }
    }
}
