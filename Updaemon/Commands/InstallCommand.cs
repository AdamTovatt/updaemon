using Updaemon.Common.Utilities;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;

namespace Updaemon.Commands
{
    /// <summary>
    /// Handles the 'install' command: downloads an app's latest release, reads its manifest, asks
    /// for missing required settings, then writes its command wrapper and service unit and starts it.
    /// </summary>
    public class InstallCommand : ICommand
    {
        private readonly IConfigManager _configManager;
        private readonly AppSettingsStore _settingsStore;
        private readonly ISecretsManager _secretsManager;
        private readonly IDistributionServiceClient _distributionClient;
        private readonly IServiceDeployer _serviceDeployer;
        private readonly IServiceManager _serviceManager;
        private readonly IFilePermissionManager _filePermissionManager;
        private readonly IPrompter _prompter;
        private readonly IOutputWriter _outputWriter;
        private readonly UpdaemonScope _scope;
        private readonly PluginProvider _pluginProvider;
        private readonly OtherScopeApps _otherScopeApps;
        private readonly PluginCredentials _credentials;

        public InstallCommand(
            IConfigManager configManager,
            AppSettingsStore settingsStore,
            ISecretsManager secretsManager,
            IDistributionServiceClient distributionClient,
            IServiceDeployer serviceDeployer,
            IServiceManager serviceManager,
            IFilePermissionManager filePermissionManager,
            IPrompter prompter,
            IOutputWriter outputWriter,
            UpdaemonScope scope,
            PluginProvider pluginProvider,
            OtherScopeApps otherScopeApps,
            PluginCredentials credentials)
        {
            _configManager = configManager;
            _settingsStore = settingsStore;
            _secretsManager = secretsManager;
            _distributionClient = distributionClient;
            _serviceDeployer = serviceDeployer;
            _serviceManager = serviceManager;
            _filePermissionManager = filePermissionManager;
            _prompter = prompter;
            _outputWriter = outputWriter;
            _scope = scope;
            _pluginProvider = pluginProvider;
            _otherScopeApps = otherScopeApps;
            _credentials = credentials;
        }

        public string Name => "install";

        public string Description => "Install an app from a distribution plugin";

        public string Usage => "updaemon install <app> --from <plugin>:<remote> [--set KEY=VALUE]... [--command <name>] [--service] [--run-as <user>]";

        public bool ChangesState => true;

        public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            InstallRequest request;
            switch (Parse(args))
            {
                case Parsed.Request parsed:
                    request = parsed.Value;
                    break;
                case Parsed.SourceMissing missing:
                    return await FailSourceMissingAsync(missing.AppName, cancellationToken);
                case Parsed.Problem problem:
                    return _outputWriter.Fail(problem.Message);
                default:
                    return _outputWriter.FailUsage(this);
            }

            string? refusal = await FindRefusalAsync(request, cancellationToken);
            if (refusal != null)
            {
                return _outputWriter.Fail(refusal);
            }

            string? otherOwner = await _otherScopeApps.FindOwnerOfAsync(request.AppName, cancellationToken);
            if (otherOwner != null)
            {
                _outputWriter.WriteError($"Warning: '{request.AppName}' is also installed for {otherOwner}. This installs a separate copy for {_scope.Owner}.");
            }

            InstalledApp app = request.CreateApp();
            DeployableVersion? staged;
            try
            {
                InstalledPluginInfo plugin = await _pluginProvider.GetOrInstallAsync(app.Plugin, cancellationToken);
                // Each try starts the plugin with its secrets as stored.
                staged = await _credentials.RunAsync(app.Plugin, () => StageLatestAsync(app, plugin, cancellationToken), handOverSavedSecret: null, cancellationToken);
            }
            catch (CredentialNeededException ex)
            {
                return _outputWriter.Fail($"{app.Name}: {ex.Message}");
            }
            catch (MissingManifestException ex)
            {
                return _outputWriter.Fail($"{ex.Message} Pass --command <name> and/or --service to install it without one.");
            }
            catch (PluginException ex)
            {
                return _outputWriter.Fail($"{app.Name}: {ex.Message}");
            }

            if (staged == null)
            {
                return _outputWriter.Fail($"{app.Source} has no released version.");
            }

            if (staged.ManifestInDirectory && app.Manifest != null)
            {
                _outputWriter.WriteLine($"The release has its own {AppManifest.FileName}; --command and --service are ignored.");
                app.Manifest = null;
            }

            AppValues? values;
            ReachableServiceManager reachable;
            try
            {
                // Known to be a service only now; the download is all there is to take back.
                reachable = await _serviceDeployer.ReachServiceManagerAsync(app.Name, staged.Manifest, cancellationToken);
                values = await CollectValuesAsync(app, staged.Manifest, request.Values, cancellationToken);
            }
            catch
            {
                _serviceDeployer.Discard(staged);
                throw;
            }

            if (values == null)
            {
                _serviceDeployer.Discard(staged);
                return 1;
            }

            return await CommitAsync(values, staged, reachable, cancellationToken);
        }

        public string GetDetailedHelp()
        {
            return """
                Install Command

                Usage:
                  updaemon install <app> --from <plugin>:<remote> [--set KEY=VALUE]...
                                   [--command <name>] [--service] [--run-as <user>]

                Description:
                  Downloads the latest release of an app and sets it up as its updaemon.json
                  describes: a command on PATH, a service, or both. Asks for each required
                  setting that has no value yet; secrets are read without echo. Without a
                  terminal, a missing required setting fails the install. When the plugin
                  needs a secret, such as a githubToken for a private repository, it is
                  asked for the same way and saved as the plugin's secret.

                Options:
                  --from <plugin>:<remote>   Where to get it, for example github:owner/repo.
                  --set KEY=VALUE            A setting value; repeat for more.
                  --command <name>           For a release without updaemon.json: link the
                                             executable named <name> on PATH as <name>.
                  --service                  For a release without updaemon.json: run the
                                             executable (named like the app) as a service.
                  --run-as <user>            System-wide only: the user the service runs as
                                             (default root).

                Examples:
                  updaemon install byteshelf --from github:AdamTovatt/byteshelf
                  updaemon install my-api --from github:owner/my-api --service --run-as myapi
                """;
        }

        /// <summary>What the arguments ask for, or what is wrong with them.</summary>
        private abstract record Parsed
        {
            public sealed record Request(InstallRequest Value) : Parsed;

            /// <summary>Only --from is missing.</summary>
            public sealed record SourceMissing(string AppName) : Parsed;

            public sealed record Problem(string Message) : Parsed;

            /// <summary>The arguments do not have the command's shape at all.</summary>
            public sealed record ShowUsage : Parsed;
        }

        private static Parsed Parse(string[] args)
        {
            ArgumentParser parser = new ArgumentParser(args, "--service");
            if (parser.GetUnknownFlags("--from", "--set", "--command", "--service", "--run-as") is [string unknown, ..])
            {
                return new Parsed.Problem($"Unknown option '{unknown}'.");
            }

            if (parser.Positionals is not [string appName])
            {
                return new Parsed.ShowUsage();
            }

            if (parser.GetFlag("--from") is not string from)
            {
                return new Parsed.SourceMissing(appName);
            }

            if (!AppManifest.IsValidAppName(appName))
            {
                return new Parsed.Problem($"'{appName}' is not a valid app name. Use letters, digits, '.', '_' and '-'; '{AppManifest.ReservedAppName}' is reserved.");
            }

            int separator = from.IndexOf(':');
            if (separator <= 0 || separator == from.Length - 1)
            {
                return new Parsed.Problem($"--from must be <plugin>:<remote>, for example github:owner/repo. Got '{from}'.");
            }

            string? command = parser.GetFlag("--command");
            if (command != null && !AppManifest.IsValidName(command))
            {
                return new Parsed.Problem($"'{command}' is not a valid command name.");
            }

            List<KeyValuePair<string, string>> values = new List<KeyValuePair<string, string>>();
            foreach (string assignment in parser.GetFlagValues("--set"))
            {
                if (!SettingAssignment.TryParse(assignment, out string key, out string value, out string? problem))
                {
                    return new Parsed.Problem(problem);
                }

                values.Add(new KeyValuePair<string, string>(key, value));
            }

            return new Parsed.Request(new InstallRequest(
                appName,
                from.Substring(0, separator),
                from.Substring(separator + 1),
                command,
                parser.HasFlag("--service"),
                parser.GetFlag("--run-as"),
                values));
        }

        /// <summary>Shows how to give the source, and what plugins it can name.</summary>
        private async Task<int> FailSourceMissingAsync(string appName, CancellationToken cancellationToken)
        {
            _outputWriter.Fail($"Say where to install {appName} from, for example: updaemon install {appName} --from github:<owner>/<repo>");
            (IReadOnlyList<string> installed, IReadOnlyList<string> onFirstUse) = await _pluginProvider.GetAvailableAsync(cancellationToken);
            IEnumerable<string> plugins = installed.Concat(onFirstUse.Select(name => $"{name} (installed on first use)"));
            _outputWriter.WriteError($"Plugins: {string.Join(", ", plugins)}.");
            return 1;
        }

        /// <summary>Why the request cannot be installed here, or null when it can.</summary>
        private async Task<string?> FindRefusalAsync(InstallRequest request, CancellationToken cancellationToken)
        {
            if (request.RunAs != null)
            {
                if (!_scope.IsSystem)
                {
                    return "--run-as is only accepted when installing system-wide (as root). A user's apps run as that user.";
                }

                if (!await _filePermissionManager.UserExistsAsync(request.RunAs, cancellationToken))
                {
                    return $"There is no user named '{request.RunAs}'.";
                }
            }

            if (await _configManager.GetAppAsync(request.AppName, cancellationToken) != null)
            {
                return $"'{request.AppName}' is already installed. Use 'updaemon update {request.AppName}' to update it.";
            }

            if (await _serviceManager.ServiceExistsAsync(request.AppName, cancellationToken))
            {
                return $"A service named '{request.AppName}' already exists. If updaemon 0.x installed it, run 'sudo updaemon migrate'; otherwise choose another app name.";
            }

            return null;
        }

        /// <summary>Stages the latest release, or returns null when the remote has none.</summary>
        private async Task<DeployableVersion?> StageLatestAsync(InstalledApp app, InstalledPluginInfo plugin, CancellationToken cancellationToken)
        {
            await _distributionClient.ConnectAsync(plugin.Path, cancellationToken);
            try
            {
                await _distributionClient.InitializeAsync(await _secretsManager.GetPluginSecretsFormattedAsync(app.Plugin, cancellationToken), cancellationToken);

                Version? latest = await _distributionClient.GetLatestVersionAsync(app.Remote, cancellationToken);
                return latest == null ? null : await _serviceDeployer.StageAsync(app, latest, _distributionClient, cancellationToken);
            }
            finally
            {
                await _distributionClient.DisposeAsync();
            }
        }

        /// <summary>
        /// The given values plus an answer for each missing required setting. Null when one is missing
        /// and there is no terminal to ask; the failure is already reported.
        /// </summary>
        private async Task<AppValues?> CollectValuesAsync(InstalledApp app, AppManifest manifest, IReadOnlyList<KeyValuePair<string, string>> given, CancellationToken cancellationToken)
        {
            AppValues values = new AppValues(app, new Dictionary<string, string>());
            foreach (KeyValuePair<string, string> value in given)
            {
                values.Set(manifest, value.Key, value.Value);
            }

            IReadOnlyList<string> missing = values.MissingRequired(manifest);
            if (missing.Count > 0 && !_prompter.IsInteractive)
            {
                _outputWriter.Fail($"{app.Name} needs {string.Join(", ", missing)}. Pass {(missing.Count == 1 ? "it" : "them")} with --set KEY=VALUE.");
                return null;
            }

            foreach (string key in missing)
            {
                values.Set(manifest, key, await AskAsync(key, manifest.Settings[key], manifest.IsSecret(key), cancellationToken));
            }

            return values;
        }

        /// <summary>
        /// Saves the app, activates and starts it. On any failure, Ctrl+C included, removes everything
        /// the install created: a half-installed app could be neither updated nor installed again.
        /// </summary>
        private async Task<int> CommitAsync(AppValues values, DeployableVersion staged, ReachableServiceManager reachable, CancellationToken cancellationToken)
        {
            InstalledApp app = values.App;
            bool dataDirectoryExisted = Directory.Exists(_scope.GetDataDirectory(app.Name));
            bool staysRunning;
            try
            {
                await _settingsStore.SaveAsync(values, cancellationToken);
                await _serviceDeployer.ActivateAsync(app, staged, reachable, cancellationToken);
                staysRunning = !staged.Manifest.IsService || await _serviceDeployer.StartAsync(app.Name, reachable, cancellationToken);
            }
            catch (Exception ex)
            {
                try
                {
                    await _serviceDeployer.RemoveAsync(app, purge: !dataDirectoryExisted, reachable, CancellationToken.None);
                    await _configManager.RemoveAppAsync(app.Name, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    _outputWriter.WriteError($"Warning: cleaning up after the failed install failed: {cleanupException.Message}");
                }

                if (ex is OperationCanceledException)
                {
                    throw;
                }

                return _outputWriter.Fail($"installing {app.Name} failed: {ex.Message}");
            }

            _outputWriter.WriteLine($"Installed {app.Name} {staged.Version} ({staged.Manifest.KindLabel}).");
            if (staged.Manifest.Command != null && !IsOnPath(_scope.BinDirectory))
            {
                _outputWriter.WriteLine($"Add {_scope.BinDirectory} to your PATH to run '{staged.Manifest.Command}'.");
            }

            if (staged.Manifest.IsService)
            {
                await LogoutWarning.WriteIfNeededAsync(_serviceManager, _outputWriter, cancellationToken);
            }

            // There is no earlier version to fall back to: the app stays installed, and the log says why.
            if (!staysRunning)
            {
                return _outputWriter.Fail($"{app.Name}: {ServiceDeployer.NotRunning(app.Name, staged.Version)}");
            }

            return 0;
        }

        private async Task<string> AskAsync(string key, SettingSpec spec, bool secret, CancellationToken cancellationToken)
        {
            string prompt = spec.Description != null ? $"{key} ({spec.Description}): " : $"{key}: ";
            while (true)
            {
                string value = await _prompter.ReadValueAsync(prompt, secret, cancellationToken);
                if (value.Length > 0 && SafeText.IsSafe(value))
                {
                    return value;
                }

                _outputWriter.WriteLine($"{key} is required.");
            }
        }

        private static bool IsOnPath(string directory)
        {
            string? path = Environment.GetEnvironmentVariable("PATH");
            return path != null && path.Split(':').Any(entry => entry.TrimEnd('/') == directory.TrimEnd('/'));
        }

        private sealed record InstallRequest(
            string AppName,
            string PluginAlias,
            string Remote,
            string? Command,
            bool Service,
            string? RunAs,
            IReadOnlyList<KeyValuePair<string, string>> Values)
        {
            public InstalledApp CreateApp() => new InstalledApp
            {
                Name = AppName,
                Plugin = PluginAlias,
                Remote = Remote,
                RunAs = RunAs,
                Manifest = Command != null || Service
                    ? new AppManifest
                    {
                        Executable = Command ?? AppName,
                        Command = Command,
                        Service = Service ? new ServiceSpec() : null,
                    }
                    : null,
            };
        }
    }
}
