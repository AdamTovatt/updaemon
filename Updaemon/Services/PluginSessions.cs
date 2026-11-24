using Updaemon.Common.Utilities;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>Opens the sessions <c>install</c> and <c>update</c> call plugins in.</summary>
    public class PluginSessions
    {
        public PluginSessions(
            IDistributionServiceClient client, ISecretsManager secretsManager, PluginProvider pluginProvider, PluginCredentials credentials, IOutputWriter outputWriter)
        {
            Client = client;
            SecretsManager = secretsManager;
            PluginProvider = pluginProvider;
            Credentials = credentials;
            OutputWriter = outputWriter;
        }

        internal IDistributionServiceClient Client { get; }

        internal ISecretsManager SecretsManager { get; }

        internal PluginProvider PluginProvider { get; }

        internal PluginCredentials Credentials { get; }

        internal IOutputWriter OutputWriter { get; }

        public PluginSession Open() => new PluginSession(this);
    }

    /// <summary>
    /// Calls to distribution plugins, one running at a time: a plugin is started by the first call to it and
    /// keeps running until a call to another, or the end of the session.
    /// </summary>
    public sealed class PluginSession : IAsyncDisposable
    {
        private readonly PluginSessions _with;
        private readonly Dictionary<string, PluginUnavailableException> _unavailable = new Dictionary<string, PluginUnavailableException>();
        private string? _running;

        internal PluginSession(PluginSessions with)
        {
            _with = with;
        }

        /// <summary>Makes a call, asking for a secret that starting the plugin or the call needs as <see cref="PluginCredentials"/> does.</summary>
        /// <exception cref="PluginUnavailableException">There is no such plugin to call; the same for every call to it.</exception>
        /// <exception cref="PluginException">The plugin refused or failed the call.</exception>
        /// <exception cref="CredentialNeededException">The plugin needs a secret.</exception>
        public Task<T> RunAsync<T>(string alias, Func<IDistributionServiceClient, Task<T>> call, CancellationToken cancellationToken = default)
        {
            return _with.Credentials.RunAsync(
                alias,
                async () =>
                {
                    await StartAsync(alias, cancellationToken);
                    return await call(_with.Client);
                },
                () => HandOverSecretsAsync(alias, cancellationToken),
                cancellationToken);
        }

        public ValueTask DisposeAsync() => new ValueTask(StopAsync());

        private async Task StartAsync(string alias, CancellationToken cancellationToken)
        {
            if (_running == alias)
            {
                return;
            }

            if (_unavailable.TryGetValue(alias, out PluginUnavailableException? known))
            {
                throw known;
            }

            await StopAsync();
            InstalledPluginInfo plugin;
            try
            {
                plugin = await _with.PluginProvider.GetOrInstallAsync(alias, cancellationToken);
            }
            catch (PluginException ex)
            {
                throw Unavailable(alias, ex.Message);
            }

            _running = alias;
            try
            {
                await _with.Client.ConnectAsync(plugin.Path, cancellationToken);
                await InitializeAsync(alias, cancellationToken);
            }
            catch (CredentialNeededException)
            {
                // Not unavailable: with the secret it starts.
                await StopAsync();
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await StopAsync();
                throw Unavailable(alias, $"the plugin '{alias}' failed to start: {ex.Message}");
            }
        }

        private PluginUnavailableException Unavailable(string alias, string message)
        {
            PluginUnavailableException unavailable = new PluginUnavailableException(message);
            _unavailable[alias] = unavailable;
            return unavailable;
        }

        /// <summary>One that was not started for want of the secret gets it when it is started.</summary>
        private Task HandOverSecretsAsync(string alias, CancellationToken cancellationToken) =>
            _running == alias ? InitializeAsync(alias, cancellationToken) : Task.CompletedTask;

        private async Task InitializeAsync(string alias, CancellationToken cancellationToken) =>
            await _with.Client.InitializeAsync(await _with.SecretsManager.GetPluginSecretsFormattedAsync(alias, cancellationToken), cancellationToken);

        private async Task StopAsync()
        {
            if (_running == null)
            {
                return;
            }

            string alias = _running;
            _running = null;
            try
            {
                await _with.Client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _with.OutputWriter.WriteError($"Warning: failed to stop the plugin '{alias}': {ex.Message}");
            }
        }
    }
}
