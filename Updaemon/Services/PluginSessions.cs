using Updaemon.Common.Utilities;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>Opens the sessions <c>install</c> and <c>update</c> call plugins in.</summary>
    public class PluginSessions
    {
        private readonly PluginSessionServices _services;

        public PluginSessions(
            Func<IDistributionServiceClient> createClient,
            ISecretsManager secretsManager,
            PluginProvider pluginProvider,
            PluginCredentials credentials,
            IOutputWriter outputWriter)
        {
            _services = new PluginSessionServices(createClient, secretsManager, pluginProvider, credentials, outputWriter);
        }

        public PluginSession Open() => new PluginSession(_services);
    }

    /// <summary>What a session calls plugins with.</summary>
    internal sealed record PluginSessionServices(
        Func<IDistributionServiceClient> CreateClient,
        ISecretsManager SecretsManager,
        PluginProvider PluginProvider,
        PluginCredentials Credentials,
        IOutputWriter OutputWriter);

    /// <summary>
    /// Calls to distribution plugins: each is started by the first call to it and keeps running until the end
    /// of the session.
    /// </summary>
    public sealed class PluginSession : IAsyncDisposable
    {
        private readonly PluginSessionServices _services;
        private readonly Dictionary<string, IDistributionServiceClient> _running = new Dictionary<string, IDistributionServiceClient>();
        private readonly Dictionary<string, PluginUnavailableException> _unavailable = new Dictionary<string, PluginUnavailableException>();

        internal PluginSession(PluginSessionServices services)
        {
            _services = services;
        }

        /// <summary>Makes a call, asking for a secret that starting the plugin or the call needs as <see cref="PluginCredentials"/> does.</summary>
        /// <exception cref="PluginUnavailableException">There is no such plugin to call; the same for every call to it.</exception>
        /// <exception cref="PluginException">The plugin refused or failed the call.</exception>
        /// <exception cref="CredentialNeededException">The plugin needs a secret.</exception>
        public Task<T> RunAsync<T>(string alias, Func<IDistributionServiceClient, Task<T>> call, CancellationToken cancellationToken = default)
        {
            return _services.Credentials.RunAsync(
                alias,
                async () => await call(await StartAsync(alias, cancellationToken)),
                () => HandOverSecretsAsync(alias, cancellationToken),
                cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            foreach ((string alias, IDistributionServiceClient client) in _running)
            {
                await StopAsync(alias, client);
            }

            _running.Clear();
        }

        private async Task<IDistributionServiceClient> StartAsync(string alias, CancellationToken cancellationToken)
        {
            if (_running.TryGetValue(alias, out IDistributionServiceClient? running))
            {
                return running;
            }

            if (_unavailable.TryGetValue(alias, out PluginUnavailableException? known))
            {
                throw known;
            }

            InstalledPluginInfo plugin;
            try
            {
                plugin = await _services.PluginProvider.GetOrInstallAsync(alias, cancellationToken);
            }
            catch (PluginException ex)
            {
                throw Unavailable(alias, ex.Message);
            }

            IDistributionServiceClient client = _services.CreateClient();
            try
            {
                await client.ConnectAsync(plugin.Path, cancellationToken);
                await InitializeAsync(alias, client, cancellationToken);
            }
            catch (Exception ex) when (ex is CredentialNeededException or OperationCanceledException)
            {
                // For want of a secret it is not unavailable: with it, it starts.
                await StopAsync(alias, client);
                throw;
            }
            catch (Exception ex)
            {
                await StopAsync(alias, client);
                throw Unavailable(alias, $"the plugin '{alias}' failed to start: {ex.Message}");
            }

            _running[alias] = client;
            return client;
        }

        private PluginUnavailableException Unavailable(string alias, string message)
        {
            PluginUnavailableException unavailable = new PluginUnavailableException(message);
            _unavailable[alias] = unavailable;
            return unavailable;
        }

        /// <summary>One that was not started for want of the secret gets it when it is started.</summary>
        private Task HandOverSecretsAsync(string alias, CancellationToken cancellationToken) =>
            _running.TryGetValue(alias, out IDistributionServiceClient? client) ? InitializeAsync(alias, client, cancellationToken) : Task.CompletedTask;

        private async Task InitializeAsync(string alias, IDistributionServiceClient client, CancellationToken cancellationToken) =>
            await client.InitializeAsync(await _services.SecretsManager.GetPluginSecretsFormattedAsync(alias, cancellationToken), cancellationToken);

        private async Task StopAsync(string alias, IDistributionServiceClient client)
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _services.OutputWriter.WriteError($"Warning: failed to stop the plugin '{alias}': {ex.Message}");
            }
        }
    }
}
