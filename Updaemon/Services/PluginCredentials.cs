using Updaemon.Common.Models;
using Updaemon.Common.Utilities;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// What updaemon does when a plugin call fails for want of a secret. At a terminal it says which
    /// case it is, asks for the secret without echo, stores it as the plugin's secret and makes the call
    /// once more. A used-up rate limit is only reported, as asking cannot end it. Without a terminal, as
    /// under the timer, the call fails in one line naming the case and the command that sets the secret.
    /// </summary>
    public class PluginCredentials
    {
        private readonly ISecretsManager _secretsManager;
        private readonly IPrompter _prompter;
        private readonly IOutputWriter _outputWriter;

        public PluginCredentials(ISecretsManager secretsManager, IPrompter prompter, IOutputWriter outputWriter)
        {
            _secretsManager = secretsManager;
            _prompter = prompter;
            _outputWriter = outputWriter;
        }

        /// <summary>Runs <paramref name="call"/>, and once more after a secret it needed is saved.</summary>
        /// <param name="pluginAlias">The plugin whose secret it needs.</param>
        /// <param name="handOverSavedSecret">
        /// Gives the saved secret to a plugin that is already running, before the call is made again.
        /// Null when the call reads the plugin's secrets itself each time.
        /// </param>
        /// <exception cref="CredentialNeededException">Still needed; the message names the case and the secret-set command.</exception>
        public async Task<T> RunAsync<T>(string pluginAlias, Func<Task<T>> call, Func<Task>? handOverSavedSecret, CancellationToken cancellationToken)
        {
            CredentialNeed need;
            try
            {
                return await call();
            }
            catch (CredentialNeededException ex) when (_prompter.IsInteractive && ex.Need.Problem != CredentialProblem.RateLimited)
            {
                _outputWriter.WriteError(ex.Message);
                need = ex.Need;
            }
            catch (CredentialNeededException ex)
            {
                throw await ExplainAsync(pluginAlias, ex, cancellationToken);
            }

            string prompt = need.Problem == CredentialProblem.Rejected
                ? $"A new {need.Secret} for the '{pluginAlias}' plugin (not shown): "
                : $"{need.Secret} for the '{pluginAlias}' plugin (not shown): ";
            string value = (await _prompter.ReadValueAsync(prompt, secret: true, cancellationToken)).Trim();
            if (value.Length == 0)
            {
                throw new CredentialNeededException(need, $"No {need.Secret} was given.{SetItWith(pluginAlias, need.Secret)}");
            }

            await _secretsManager.SetSecretAsync(pluginAlias, need.Secret, value, cancellationToken);
            _outputWriter.WriteLine($"Saved the '{pluginAlias}' plugin's {need.Secret}. Trying again...");
            if (handOverSavedSecret != null)
            {
                await handOverSavedSecret();
            }

            // Once: a second refusal is reported rather than asked about again.
            try
            {
                return await call();
            }
            catch (CredentialNeededException ex)
            {
                throw await ExplainAsync(pluginAlias, ex, cancellationToken);
            }
        }

        /// <summary>The refusal as one line that also says what to run.</summary>
        private async Task<CredentialNeededException> ExplainAsync(string pluginAlias, CredentialNeededException refusal, CancellationToken cancellationToken)
        {
            CredentialNeed need = refusal.Need;
            string advice = need.Problem != CredentialProblem.RateLimited
                ? SetItWith(pluginAlias, need.Secret)
                : await _secretsManager.GetSecretAsync(pluginAlias, need.Secret, cancellationToken) == null
                    ? $" A {need.Secret} raises the limit; set one with 'updaemon secret-set {pluginAlias} {need.Secret}'."
                    : string.Empty;
            return new CredentialNeededException(need, refusal.Message + advice);
        }

        private static string SetItWith(string pluginAlias, string secret) =>
            $" Set it with 'updaemon secret-set {pluginAlias} {secret}'.";
    }
}
