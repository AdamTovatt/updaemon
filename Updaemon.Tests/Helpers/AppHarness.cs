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

        public AppHarness(bool isSystem = false)
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
                Output);
        }

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

        public InstallCommand Install() => new InstallCommand(Config, SettingsStore, Secrets, Client, Deployer, ServiceManager, Permissions, Prompter, Output, Scope);

        public UpdateCommand Update() => new UpdateCommand(Config, SettingsStore, Secrets, Client, Output, Deployer);

        public SetCommand Set() => new SetCommand(Config, SettingsStore, Deployer, Output);

        public UnsetCommand Unset() => new UnsetCommand(Config, SettingsStore, Deployer, Output);

        public SetArgsCommand SetArgs() => new SetArgsCommand(Config, Deployer, Output);

        public RemoveCommand Remove() => new RemoveCommand(Config, Deployer, Output, Scope);

        public async Task AddPluginAsync(string alias = "github")
        {
            string path = _temp.CreateTempFile($"plugin-binaries/{alias}", "plugin");
            await Config.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = alias, Path = path });
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

        public void Dispose()
        {
            _temp.Dispose();
        }
    }
}
