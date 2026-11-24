using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>Downloads a package's latest release for <c>install</c>, with the plugin it comes from.</summary>
    public class ReleaseStager
    {
        private readonly PluginSessions _sessions;

        public ReleaseStager(PluginSessions sessions)
        {
            _sessions = sessions;
        }

        /// <summary>What <paramref name="stage"/> makes of the latest release.</summary>
        /// <exception cref="PluginException">There is no plugin to download with, it does not start, or it failed.</exception>
        /// <exception cref="NoReleaseException">The source has no released version.</exception>
        /// <exception cref="Common.Utilities.CredentialNeededException">The plugin needs a secret.</exception>
        public async Task<T> StageLatestAsync<T>(PackageSource source, Func<Version, IDistributionServiceClient, Task<T>> stage, CancellationToken cancellationToken = default)
        {
            await using (PluginSession session = _sessions.Open())
            {
                return await session.RunAsync(
                    source.Plugin,
                    async client =>
                    {
                        Version latest = await client.GetLatestVersionAsync(source.Remote, cancellationToken) ?? throw new NoReleaseException(source);
                        return await stage(latest, client);
                    },
                    cancellationToken);
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
