using Updaemon.Common.Utilities;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'dist-install' command to install a distribution service plugin.
    /// </summary>
    public class DistInstallCommand : ICommand
    {
        private readonly IOutputWriter _outputWriter;
        private readonly IPluginInstaller _pluginInstaller;
        private readonly IGitHubPluginReleases _gitHubPluginReleases;
        private readonly UpdaemonVersion _updaemonVersion;
        private readonly PluginCredentials _credentials;

        /// <param name="updaemonVersion">The running updaemon's version: registry plugins come from its release.</param>
        public DistInstallCommand(IOutputWriter outputWriter, IPluginInstaller pluginInstaller, IGitHubPluginReleases gitHubPluginReleases, UpdaemonVersion updaemonVersion, PluginCredentials credentials)
        {
            _outputWriter = outputWriter;
            _pluginInstaller = pluginInstaller;
            _gitHubPluginReleases = gitHubPluginReleases;
            _updaemonVersion = updaemonVersion;
            _credentials = credentials;
        }

        public string Name => "dist-install";

        public string Description => "Install a distribution service plugin";

        public string Usage => PluginSource.InstallUsage;

        public bool ChangesState => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentParser parser = new ArgumentParser(args);

            string? alias = parser.GetFlag("--as");
            string? urlOrName = parser.GetFirstNonFlag();

            if (string.IsNullOrEmpty(urlOrName))
            {
                _outputWriter.WriteError("Error: 'dist-install' command requires a plugin name or URL");
                _outputWriter.WriteLine($"Usage: {Usage}");
                return 1;
            }

            PluginInstallResult result;
            try
            {
                PluginSource source = await PluginSource.ParseAsync(
                    urlOrName,
                    _updaemonVersion,
                    (owner, repo) => WithGitHubTokenAsync(() => _gitHubPluginReleases.GetLatestTagAsync(owner, repo, cancellationToken), cancellationToken));

                _outputWriter.WriteLine(source is PluginSource.GitHub github
                    ? $"Installing distribution plugin: {source}, release {github.Tag}"
                    : $"Installing distribution plugin: {source}");

                result = source is PluginSource.GitHub
                    ? await WithGitHubTokenAsync(() => _pluginInstaller.InstallAsync(source, alias, cancellationToken), cancellationToken)
                    : await _pluginInstaller.InstallAsync(source, alias, cancellationToken);
            }
            catch (Exception ex) when (ex is FormatException or CredentialNeededException)
            {
                return _outputWriter.Fail(ex.Message);
            }

            _outputWriter.WriteLine($"Saved plugin to: {result.Plugin.Path}");
            _outputWriter.WriteLine(result.ReplacedExisting
                ? $"Distribution plugin '{result.Plugin.Alias}' replaced successfully"
                : $"Distribution plugin '{result.Plugin.Alias}' installed successfully");
            _outputWriter.WriteLine($"  Name: {result.ServiceInformation.FullName}");
            _outputWriter.WriteLine($"  Version: {result.ServiceInformation.Version}");
            _outputWriter.WriteLine($"  Description: {result.ServiceInformation.Description}");
            return 0;
        }

        /// <summary>A github: source's call, which reads the 'github' plugin's token itself; asked for here when it needs one.</summary>
        private Task<T> WithGitHubTokenAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken) =>
            _credentials.RunAsync(GitHubPluginReleases.TokenPluginAlias, call, handOverSavedSecret: null, cancellationToken);

        public string GetDetailedHelp()
        {
            return $"""
                Dist-Install Command

                Usage:
                  {PluginSource.InstallUsage}

                Description:
                  Installs a distribution service plugin, by registry name (e.g. "github"),
                  from a GitHub repository's releases, or from a URL.

                  A registry plugin comes from this updaemon version's own release, built for
                  this machine, and follows updaemon's updates.

                  github:<owner>/<repo> installs the one asset named *-<rid> (e.g.
                  *-linux-arm64) from the repository's latest release. It must match its line
                  in the release's SHA256SUMS. While self-update is on, 'updaemon update'
                  installs the repository's latest release whenever it differs from the one
                  installed. A release refused for its checksum, its build or its plugin
                  protocol is skipped until another is latest; run dist-install again to
                  retry it once it is fixed. A private repository needs the 'github'
                  plugin's githubToken; at a terminal, dist-install asks for it.

                  A URL plugin is not verified and is never updated; install it again to
                  replace it.

                  If --as is not given, a registry plugin is installed under its name and
                  any other under its default alias. A plugin already installed under the
                  alias is replaced; if the new one fails to start, the installed one is kept.

                Examples:
                  updaemon dist-install github
                  updaemon dist-install github:someone/updaemon-s3
                  updaemon dist-install --as github https://example.com/plugin
                """;
        }
    }
}
