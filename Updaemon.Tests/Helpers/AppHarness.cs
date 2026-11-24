using Updaemon.Commands;
using Updaemon.Configuration;
using Updaemon.Conversion;
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

        /// <summary>A host of extensions: one it knows, loaded from the folder in SHELF_PLUGINS.</summary>
        public const string HostManifest = """
            {
              "executable": "bin/shelf",
              "command": "shelf",
              "service": { "args": ["serve"] },
              "extensions": {
                "variable": "SHELF_PLUGINS",
                "check": ["--check-plugin"],
                "known": { "images": "github:owner/shelf/images.zip", "audio": "other:audio" }
              }
            }
            """;

        private readonly TempFileHelper _temp = new TempFileHelper();

        /// <summary>The health check's clock: it never waits, and records what it would have.</summary>
        public InstantTimeProvider Time { get; } = new InstantTimeProvider();

        public AppHarness(bool isSystem = false, bool isMacOS = false)
        {
            Scope = TestScope.Create(_temp.TempDirectory, isSystem, isMacOS);
            OtherScope = TestScope.Create(Path.Combine(_temp.TempDirectory, "other"), !isSystem);
            Config = new ConfigManager(Scope);
            Secrets = new SecretsManager(Scope, Permissions);
            SavedSecrets = new WatchedSecretsManager(Secrets);
            SavedConfig = new WatchedConfigManager(Config);
            EntryStore = new AppEntryStore(SavedConfig, SavedSecrets);
            Files = new AppFileWriter(Scope, Permissions, new UnitFileManager(Scope), ServiceManager, Output);
            Links = new WatchedSymlinkManager(new SymlinkManager());
            Versions = new VersionStore(Scope, Links, Permissions, Output);
            Deployer = new ServiceDeployer(
                Scope,
                Versions,
                new ExecutableDetector(),
                Permissions,
                Files,
                ServiceManager,
                Secrets,
                Output,
                Time);
            SwitchRestorer restorer = new SwitchRestorer(Versions, Deployer, ServiceManager);
            Preparer = new AppPreparer(restorer, Deployer, ServiceManager, EntryStore, Output);
            Readiness = new SwitchReadiness(Versions, Deployer, EntryStore);
            Switcher = new AppSwitcher(Versions, Readiness, restorer, Deployer, ServiceManager, Output);
            SettingsChanger = new SettingsChanger(Preparer, Deployer, ServiceManager, Output);
            Check = new ExtensionCheck(Scope, Versions) { Run = RunCheckAsync };
            RunLog = new UpdateRunLog(Scope);
            PluginUrlResolver = new GitHubPluginUrlResolver(new Dictionary<string, string> { ["github"] = "Updaemon.GithubDistributionService" }, Scope.Platform);
            PluginInstaller = new PluginInstaller(PluginUrlResolver, GitHubPlugins, PluginDownloader, Config, Scope);
            Plugins = new PluginProvider(Config, PluginInstaller, PluginUrlResolver, RunningVersion, Output);
        }

        /// <summary>For calling the deployer directly, as a command does once it has checked the service manager.</summary>
        public ReachableServiceManager Reachable { get; } = new ReachableServiceManager();

        public MockGitHubPluginReleases GitHubPlugins { get; } = new MockGitHubPluginReleases();

        public PluginInstaller PluginInstaller { get; }

        public MockUpdaemonReleases Releases { get; } = new MockUpdaemonReleases();

        /// <summary>
        /// The file the updaemon under test runs from. By default not the scope's own, so update's
        /// self-update step skips; tests of self-update point it at the scope's updaemon.
        /// </summary>
        public RunningExecutable Executable { get; set; } = new RunningExecutable("/nonexistent/updaemon");

        public SelfUpdater SelfUpdater() => new SelfUpdater(Scope, Config, Releases, new SymlinkManager(), Output, RunningVersion, Executable);

        /// <summary>Runs <paramref name="updater"/> (the default one when null) as an update run does; true when nothing failed.</summary>
        public async Task<bool> SelfUpdateAsync(SelfUpdater? updater = null)
        {
            UpdateFailures failures = new UpdateFailures(Output);
            await (updater ?? SelfUpdater()).RunAsync(failures);
            return failures.All.Count == 0;
        }

        /// <summary>Updates the github: plugins as an update run does; true when nothing failed.</summary>
        public async Task<bool> UpdateGitHubPluginsAsync()
        {
            UpdateFailures failures = new UpdateFailures(Output);
            await GitHubPluginUpdater().RunAsync(failures);
            return failures.All.Count == 0;
        }

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

        public AppPreparer Preparer { get; }

        public SwitchReadiness Readiness { get; }

        public AppSwitcher Switcher { get; }

        public SettingsChanger SettingsChanger { get; }

        /// <summary>What an install and a change to an installed app save the entry with.</summary>
        public WatchedConfigManager SavedConfig { get; }

        /// <summary>What they save the secrets with.</summary>
        public WatchedSecretsManager SavedSecrets { get; }

        public VersionStore Versions { get; }

        public WatchedSymlinkManager Links { get; }

        /// <summary>The directory everything is under.</summary>
        public string Root => _temp.TempDirectory;

        /// <summary>
        /// Called at each step of a change that a run can be killed at: before and after each save of an app's
        /// entry and of its secrets, after each link, at each tree made readable, each reload and before each
        /// restart, start, stop, enabling and disabling of the service.
        /// </summary>
        public Action? OnStep
        {
            set
            {
                SavedConfig.OnSave = value == null ? null : _ => value();
                SavedConfig.OnSaved = value == null ? null : _ => value();
                SavedSecrets.OnSave = value;
                SavedSecrets.OnSaved = value;
                Links.OnLinked = value;
                Permissions.OnMakeTreeReadable = value == null ? null : _ => value();
                ServiceManager.OnDaemonReload = value;
                ServiceManager.OnRestart = value;
                ServiceManager.OnServiceChange = value;
            }
        }

        public ExtensionCheck Check { get; }

        public UpdateRunLog RunLog { get; }

        public PluginProvider Plugins { get; }

        public MockPluginDownloader PluginDownloader { get; } = new MockPluginDownloader();

        public MockTimerManager Timer { get; } = new MockTimerManager();

        public UpdaemonScope Scope { get; }

        /// <summary>The scope this run is not in, which install looks in for the same app name.</summary>
        public UpdaemonScope OtherScope { get; }

        public ConfigManager Config { get; }

        public SecretsManager Secrets { get; }

        public AppEntryStore EntryStore { get; }

        public ServiceDeployer Deployer { get; }

        public AppFileWriter Files { get; }

        public MockServiceManager ServiceManager { get; } = new MockServiceManager();

        public MockFilePermissionManager Permissions { get; } = new MockFilePermissionManager();

        public MockDistributionServiceClient Client { get; } = new MockDistributionServiceClient();

        public MockPrompter Prompter { get; } = new MockPrompter();

        public MockOutputWriter Output { get; } = new MockOutputWriter();

        public InstallCommand Install() => new InstallCommand(
            Config, EntryStore, Stager(), Versions, Deployer, ServiceManager, Permissions, Prompter, Output, Scope, Plugins, OtherScopeApps(), ExtensionInstaller());

        private ReleaseStager Stager() => new ReleaseStager(Sessions());

        private OtherScopeApps OtherScopeApps() => new OtherScopeApps(new ConfigManager(OtherScope), OtherScope.Owner);

        private ExtensionInstaller ExtensionInstaller() => new ExtensionInstaller(Config, Versions, Deployer, Preparer, Switcher, Check, Stager(), OtherScopeApps(), Scope, Output);

        public UpdateCommand Update() => new UpdateCommand(Config, Preparer, AppUpdater(), GitHubPluginUpdater(), SelfUpdater(), RunLog, Output, Time);

        private AppUpdater AppUpdater() => new AppUpdater(Sessions(), Versions, Deployer, Readiness, Switcher, Check, Output);

        private PluginSessions Sessions() => new PluginSessions(() => Client, Secrets, Plugins, Credentials(), Output);

        public GitHubPluginUpdater GitHubPluginUpdater() => new GitHubPluginUpdater(Config, GitHubPlugins, PluginInstaller, Output, Credentials());

        public DistInstallCommand DistInstall() => new DistInstallCommand(Output, PluginInstaller, GitHubPlugins, RunningVersion, Credentials());

        public PluginCredentials Credentials() => new PluginCredentials(Secrets, Prompter, Output);

        public TimerCommand TimerCommand() => new TimerCommand(Timer, ServiceManager, Config, Output);

        public DistListCommand DistList() => new DistListCommand(Config, Output, () => Client, RunningVersion);

        public RollbackCommand RollbackCommand() => new RollbackCommand(Config, Versions, Deployer, Preparer, Readiness, Switcher, Check, Output);

        public StatusCommand Status() => new StatusCommand(Config, Versions, Deployer, ServiceManager, Timer, Output, Scope, RunningVersion, RunLog, Time);

        public LogsCommand Logs() => new LogsCommand(Config, Deployer, ServiceManager, Output);

        public SetCommand Set() => new SetCommand(Config, SettingsChanger, Output);

        public UnsetCommand Unset() => new UnsetCommand(Config, SettingsChanger, Output);

        public SetArgsCommand SetArgs() => new SetArgsCommand(Config, SettingsChanger, Output);

        public SetRestartCommand SetRestart() => new SetRestartCommand(Config, Output, Deployer);

        public RemoveCommand Remove() => new RemoveCommand(Config, Deployer, Preparer, Switcher, RunLog, Output, Scope);

        public ZeroXConverter Converter() => new ZeroXConverter(
            Scope, Config, Versions, Deployer, ServiceManager, Timer, new ExecutableDetector(), new SymlinkManager(), Permissions, Files, Plugins, PluginUrlResolver, Output);

        /// <summary>Every check a host was asked to run, in order.</summary>
        internal List<ProcessInvocation> Checks { get; } = new List<ProcessInvocation>();

        /// <summary>How a host's check answers; by default it accepts.</summary>
        internal Func<ProcessInvocation, ProcessRunner.Result> AnswerCheck { get; set; } = _ => new ProcessRunner.Result(0, string.Empty, string.Empty);

        private Task<ProcessRunner.Result> RunCheckAsync(ProcessInvocation invocation, CancellationToken cancellationToken)
        {
            Checks.Add(invocation);
            return Task.FromResult(AnswerCheck(invocation));
        }

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

        /// <summary>Publishes a release of an extension: one file, <c>plugin.dll</c>, holding <paramref name="version"/>.</summary>
        public void AddExtensionRelease(string remote, string version)
        {
            Client.AddRelease(remote, Version.Parse(version), directory => File.WriteAllText(Path.Combine(directory, "plugin.dll"), version));
        }

        /// <summary>Publishes a release of the host <c>shelf</c>, whose executable prints SHELF_PLUGINS.</summary>
        public void AddHostRelease(string version, string manifest = HostManifest) => AddRelease("owner/shelf", version, manifest, "bin/shelf", "SHELF_PLUGINS");

        /// <summary>Installs the host <c>shelf</c> 1.0.0, in the system scope as the user <c>daemon</c>, which every machine has, and publishes its extension images 1.0.0.</summary>
        public async Task InstallHostAsync(string manifest = HostManifest, params string[] options)
        {
            await AddPluginAsync();
            Permissions.ExistingUsers.Add("daemon");
            AddHostRelease("1.0.0", manifest);
            AddExtensionRelease("owner/shelf/images.zip", "1.0.0");
            string[] runAs = Scope.IsSystem ? new[] { "--run-as", "daemon" } : Array.Empty<string>();
            Assert.Equal(0, await Install().ExecuteAsync(["shelf", "--from", "github:owner/shelf", .. runAs, .. options]));
        }

        /// <summary>The host and its extension images, both at 1.0.0.</summary>
        public async Task InstallHostWithImagesAsync(string manifest = HostManifest)
        {
            await InstallHostAsync(manifest);
            Assert.Equal(0, await Install().ExecuteAsync(new[] { "shelf/images" }));
            ForgetCalls();
        }

        /// <summary>A run killed while it installed the extension images into the host, which is installed.</summary>
        public async Task KilledInstallOfImagesAsync()
        {
            ServiceManager.OnRestart = KeepForCutOff;
            Assert.Equal(0, await Install().ExecuteAsync(new[] { "shelf/images" }));
            CutOff();
        }

        /// <summary>Forgets what was printed, checked and called so far.</summary>
        public void ForgetCalls()
        {
            Output.Clear();
            Checks.Clear();
            ServiceManager.MethodCalls.Clear();
            Client.MethodCalls.Clear();
        }

        /// <summary>The host's check refuses, always or only <paramref name="which"/>, with this output.</summary>
        internal void Refuse(string because, Func<ProcessInvocation, bool>? which = null) =>
            AnswerCheck = check => which == null || which(check) ? new ProcessRunner.Result(1, string.Empty, because) : new ProcessRunner.Result(0, string.Empty, string.Empty);

        /// <summary>How often the host's service was restarted since the calls were last cleared.</summary>
        public int HostRestarts() => ServiceManager.MethodCalls.Count(call => call == MockServiceManager.Call(nameof(MockServiceManager.RestartServiceAsync), "shelf"));

        public async Task<InstalledApp> HostAsync() => (await Config.GetAppAsync("shelf"))!;

        /// <summary>What is in the folder the host loads its extensions from, as <c>id -> version</c>.</summary>
        public IReadOnlyList<string> LinkedExtensions() => Directory.GetFileSystemEntries(Scope.GetExtensionsDirectory("shelf"))
            .Order(StringComparer.Ordinal)
            .Select(link => $"{Path.GetFileName(link)} -> {File.ReadAllText(Path.Combine(link, "plugin.dll"))}")
            .ToList();

        /// <summary>Runs <paramref name="fault"/> on the download directory once the release's files are written.</summary>
        public void AfterDownloading(string remote, string version, Action<string> fault)
        {
            string key = $"{remote}:{version}";
            Action<string> write = Client.ReleaseWriters[key];
            Client.ReleaseWriters[key] = directory =>
            {
                write(directory);
                fault(directory);
            };
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
            app.RecordSwitch(from: version, to: await Versions.GetCurrentAsync(app.PackageName));
            await Config.SaveAppAsync(app);
        }

        /// <summary>
        /// Every directory, file and link under the harness's directory, with each file's content and
        /// each link's target. Equal snapshots mean nothing was written.
        /// </summary>
        public IReadOnlyList<string> Snapshot()
        {
            EnumerationOptions options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
            return Directory.EnumerateFileSystemEntries(_temp.TempDirectory, "*", options)
                .Order(StringComparer.Ordinal)
                .Select(path => new FileInfo(path).LinkTarget is string target ? $"{path} -> {target}"
                    : Directory.Exists(path) ? path + "/"
                    : $"{path}: {File.ReadAllText(path)}")
                .ToList();
        }

        /// <summary>Fails naming what changed since <paramref name="before"/>, a <see cref="Snapshot"/>.</summary>
        public void AssertUnchangedSince(IReadOnlyList<string> before)
        {
            IReadOnlyList<string> after = Snapshot();
            Assert.True(after.SequenceEqual(before), Difference(after, before));
        }

        /// <summary>What <paramref name="after"/> has that <paramref name="before"/> has not, and the other way round, a line each.</summary>
        public static string Difference(IReadOnlyList<string> after, IReadOnlyList<string> before) =>
            string.Join('\n', before.Except(after).Select(entry => "gone: " + entry).Concat(after.Except(before).Select(entry => "new: " + entry)));

        private string? _cutOff;
        private Dictionary<string, bool> _runningAtCutOff = new Dictionary<string, bool>();
        private List<string> _enabledAtCutOff = new List<string>();

        /// <summary>
        /// Keeps what is on disk and what its services are now, the first time it is called: what a run killed here leaves.
        /// <see cref="CutOff"/> then goes back to it.
        /// </summary>
        public void KeepForCutOff()
        {
            if (_cutOff == null)
            {
                _cutOff = _temp.TempDirectory + "_cutoff";
                Copy(_temp.TempDirectory, _cutOff);
                _runningAtCutOff = new Dictionary<string, bool>(ServiceManager.ServiceRunningStates);
                _enabledAtCutOff = ServiceManager.EnabledServices.ToList();
            }
        }

        /// <summary>Leaves what <see cref="KeepForCutOff"/> kept, as if the run had been killed there.</summary>
        public void CutOff()
        {
            Assert.NotNull(_cutOff);
            Directory.Delete(_temp.TempDirectory, recursive: true);
            Directory.Move(_cutOff, _temp.TempDirectory);
            _cutOff = null;
            ServiceManager.ServiceRunningStates.Clear();
            foreach ((string service, bool running) in _runningAtCutOff)
            {
                ServiceManager.ServiceRunningStates[service] = running;
            }

            ServiceManager.EnabledServices.Clear();
            ServiceManager.EnabledServices.UnionWith(_enabledAtCutOff);
            OnStep = null;
            Output.Clear();
            ServiceManager.MethodCalls.Clear();
        }

        private static void Copy(string from, string to)
        {
            using (System.Diagnostics.Process copy = System.Diagnostics.Process.Start("cp", new[] { "-a", from, to }))
            {
                copy.WaitForExit();
                Assert.Equal(0, copy.ExitCode);
            }
        }

        public void Dispose()
        {
            _temp.Dispose();
            if (_cutOff != null)
            {
                Directory.Delete(_cutOff, recursive: true);
            }
        }
    }
}
