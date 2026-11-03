using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'dist-install' command to install a distribution service plugin.
    /// </summary>
    public class DistInstallCommand : ICommand
    {
        private readonly IOutputWriter _outputWriter;
        private readonly IPluginInstaller _pluginInstaller;
        private readonly UpdaemonVersion _updaemonVersion;

        /// <param name="updaemonVersion">The running updaemon's version: registry plugins come from its release.</param>
        public DistInstallCommand(IOutputWriter outputWriter, IPluginInstaller pluginInstaller, UpdaemonVersion updaemonVersion)
        {
            _outputWriter = outputWriter;
            _pluginInstaller = pluginInstaller;
            _updaemonVersion = updaemonVersion;
        }

        public string Name => "dist-install";

        public string Description => "Install a distribution service plugin";

        public string Usage => "updaemon dist-install [--as <alias>] <plugin-name|url>";

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

            bool isUrl = urlOrName.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || urlOrName.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            PluginSource source = isUrl ? new PluginSource.Url(urlOrName) : new PluginSource.Registry(urlOrName, _updaemonVersion);

            _outputWriter.WriteLine($"Installing distribution plugin: {source}");

            PluginInstallResult result = await _pluginInstaller.InstallAsync(source, alias, cancellationToken);

            _outputWriter.WriteLine($"Saved plugin to: {result.Plugin.Path}");
            _outputWriter.WriteLine(result.ReplacedExisting
                ? $"Distribution plugin '{result.Plugin.Alias}' replaced successfully"
                : $"Distribution plugin '{result.Plugin.Alias}' installed successfully");
            _outputWriter.WriteLine($"  Name: {result.ServiceInformation.FullName}");
            _outputWriter.WriteLine($"  Version: {result.ServiceInformation.Version}");
            _outputWriter.WriteLine($"  Description: {result.ServiceInformation.Description}");
            return 0;
        }

        public string GetDetailedHelp()
        {
            return """
                Dist-Install Command

                Usage:
                  updaemon dist-install [--as <alias>] <plugin-name|url>

                Description:
                  Installs a distribution service plugin, from a URL or by registry name
                  (e.g. "github"). A registry plugin comes from this updaemon version's own
                  release, built for this machine. If --as is not given, a registry plugin is
                  installed under its name and a URL plugin under its default alias. A plugin
                  already installed under the alias is replaced; if the new one fails to
                  start, the installed one is kept.

                Examples:
                  updaemon dist-install github
                  updaemon dist-install --as github https://example.com/plugin
                  updaemon dist-install https://example.com/plugin
                """;
        }
    }
}
