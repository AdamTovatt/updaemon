using Updaemon.Commands;
using Updaemon.Configuration;
using Updaemon.IO;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Helpers
{
    /// <summary>
    /// The real config, secrets, deploy and unit code against a temp scope, with fakes for the
    /// plugin, the service manager, file ownership and the terminal.
    /// </summary>
    public sealed class AppHarness : IDisposable
    {
        public const string ByteShelfManifest = """
            {
              "executable": "bin/byteshelf",
              "command": "byteshelf",
              "service": { "args": ["sync", "run"] },
              "settings": {
                "BYTESHELF_URL": { "description": "Server URL", "required": true },
                "BYTESHELF_API_KEY": { "description": "API key", "required": true, "secret": true },
                "BYTESHELF_LOG_LEVEL": { "description": "Log level", "default": "info" }
              }
            }
            """;

        private readonly TempFileHelper _temp = new TempFileHelper();

        /// <param name="healthCheckWait">Zero by default: the health check then takes two readings without waiting.</param>
        public AppHarness(bool isSystem = false, TimeSpan healthCheckWait = default)
        {
            Scope = TestScope.Create(_temp.TempDirectory, isSystem);
            Config = new ConfigManager(Scope);
            Secrets = new SecretsManager(Scope);
            SettingsStore = new AppSettingsStore(Config, Secrets);
            Deployer = new ServiceDeployer(
                Scope,
                new SymlinkManager(),
                new ExecutableDetector(),
                Permissions,
                new AppFileWriter(Scope, Permissions, new UnitFileManager(Scope), ServiceManager, Secrets, Output),
                ServiceManager,
                Secrets,
                Output,
                new HealthCheckWait(healthCheckWait));
            Rollback = new AppRollback(Deployer, SettingsStore, Config);
            PluginUrlResolver = new GitHubPluginUrlResolver(new Dictionary<string, string> { ["github"] = "Updaemon.GithubDistributionService" }, Scope.Platform);
            Plugins = new PluginProvider(Config, new PluginInstaller(PluginUrlResolver, PluginDownloader, Config, Scope), PluginUrlResolver, RunningVersion, Output);
        }

        public MockUpdaemonReleases Releases { get; } = new MockUpdaemonReleases();

        /// <summary>
        /// The file the updaemon under test runs from. By default not the scope's own, so update's
        /// self-update step skips; tests of self-update point it at the scope's updaemon.
        /// </summary>
        public RunningExecutable Executable { get; set; } = new RunningExecutable("/nonexistent/updaemon");

        public SelfUpdater SelfUpdater() => new SelfUpdater(Scope, Config, Releases, new SymlinkManager(), Output, RunningVersion, Executable);

        public SelfUpdater SelfUpdater(TimeSpan smokeRunTimeout) =>
            new SelfUpdater(Scope, Config, Releases, new SymlinkManager(), Output, RunningVersion, Executable) { SmokeRunTimeout = smokeRunTimeout };

        /// <summary>
        /// Installs <see cref="RunningVersion"/> as the scope's updaemon, as install.sh does, and runs
        /// it: self-update then replaces it.
        /// </summary>
        public void RunTheScopesOwnUpdaemon()
        {
            string binary = Scope.GetUpdaemonBinaryPath(RunningVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
            File.WriteAllText(binary, $"#!/bin/sh\necho {RunningVersion}\n");
            File.SetUnixFileMode(binary, AtomicFile.Executable);
            Directory.CreateDirectory(Scope.BinDirectory);
            File.CreateSymbolicLink(Scope.UpdaemonCommandPath, binary);
            Executable = new RunningExecutable(binary);
        }

        public GitHubPluginUrlResolver PluginUrlResolver { get; }

        public static UpdaemonVersion RunningVersion { get; } = UpdaemonVersion.Of(1, 0, 0);

        public AppRollback Rollback { get; }

        public PluginProvider Plugins { get; }

        public MockPluginDownloader PluginDownloader { get; } = new MockPluginDownloader();

        public MockTimerManager Timer { get; } = new MockTimerManager();

        public UpdaemonScope Scope { get; }

        public ConfigManager Config { get; }

        public SecretsManager Secrets { get; }

        public AppSettingsStore SettingsStore { get; }

        public ServiceDeployer Deployer { get; }

        public MockServiceManager ServiceManager { get; } = new MockServiceManager();

        public MockFilePermissionManager Permissions { get; } = new MockFilePermissionManager();

        public MockDistributionServiceClient Client { get; } = new MockDistributionServiceClient();

        public MockPrompter Prompter { get; } = new MockPrompter();

        public MockOutputWriter Output { get; } = new MockOutputWriter();

        public InstallCommand Install() => new InstallCommand(
            Config, SettingsStore, Secrets, Client, Deployer, ServiceManager, Permissions, Prompter, Output, Scope, Plugins);

        public UpdateCommand Update() => new UpdateCommand(Config, SettingsStore, Secrets, Client, Output, Deployer, Rollback, Plugins, SelfUpdater());

        public RollbackCommand RollbackCommand() => new RollbackCommand(Config, Deployer, Rollback, Output);

        public StatusCommand Status() => new StatusCommand(Config, Deployer, ServiceManager, Timer, Output, Scope, RunningVersion);

        public LogsCommand Logs() => new LogsCommand(Config, Deployer, ServiceManager, Output);

        public SetCommand Set() => new SetCommand(Config, SettingsStore, Deployer, Output);

        public UnsetCommand Unset() => new UnsetCommand(Config, SettingsStore, Deployer, Output);

        public SetArgsCommand SetArgs() => new SetArgsCommand(Config, Deployer, Output);

        public RemoveCommand Remove() => new RemoveCommand(Config, Deployer, Output, Scope);

        public async Task AddPluginAsync(string alias = "github")
        {
            string path = _temp.CreateTempFile($"plugin-binaries/{alias}", "plugin");
            await Config.AddOrUpdatePluginAsync(PluginRecords.Installed(alias, path));
        }

        /// <summary>
        /// Publishes a release with an executable that prints the given variables and its arguments,
        /// and, when <paramref name="manifest"/> is given, an updaemon.json.
        /// </summary>
        public void AddRelease(string remote, string version, string? manifest, string executable = "bin/byteshelf", params string[] printedVariables)
        {
            Client.AddRelease(remote, Version.Parse(version), directory =>
            {
                string executablePath = Path.Combine(directory, executable);
                Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
                File.WriteAllText(executablePath, Shell.PrintEnvironmentScript(printedVariables));
                File.SetUnixFileMode(executablePath, AtomicFile.Executable);
                if (manifest != null)
                {
                    File.WriteAllText(Path.Combine(directory, AppManifest.FileName), manifest);
                }
            });
        }

        /// <summary>Installs ByteShelf 1.0.0 from its manifest with every required setting given.</summary>
        public async Task<int> InstallByteShelfAsync()
        {
            await AddPluginAsync();
            AddRelease("owner/byteshelf", "1.0.0", ByteShelfManifest, "bin/byteshelf", "BYTESHELF_URL", "BYTESHELF_API_KEY", "BYTESHELF_LOG_LEVEL", AppManifest.DataDirectoryVariable);
            return await Install().ExecuteAsync(new[]
            {
                "byteshelf", "--from", "github:owner/byteshelf",
                "--set", "BYTESHELF_URL=https://shelf", "--set", "BYTESHELF_API_KEY=key1",
            });
        }

        /// <summary>
        /// ByteShelf installed at 1.0.0; 1.1.0 failed its health check, so it was skipped and 1.0.0 ran
        /// again; then 1.2.0 was installed. All three are kept.
        /// </summary>
        public async Task InstallByteShelfPastACrashingReleaseAsync()
        {
            await InstallByteShelfAsync();
            AddRelease("owner/byteshelf", "1.1.0", ByteShelfManifest);
            ServiceManager.CrashOnNextStart.Add("byteshelf");
            await Update().ExecuteAsync(Array.Empty<string>());
            AddRelease("owner/byteshelf", "1.2.0", ByteShelfManifest);
            await Update().ExecuteAsync(Array.Empty<string>());
            Output.Clear();
        }

        /// <summary>Records <paramref name="version"/> as skipped, as rolling back from it to the current version does.</summary>
        public async Task SkipAsync(string appName, Version version)
        {
            InstalledApp app = (await Config.GetAppAsync(appName))!;
            app.RecordSwitch(from: version, to: await Deployer.GetCurrentVersionAsync(appName));
            await Config.SaveAppAsync(app);
        }

        public void Dispose()
        {
            _temp.Dispose();
        }
    }
}
