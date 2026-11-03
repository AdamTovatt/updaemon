using Microsoft.Extensions.DependencyInjection;
using Updaemon.Commands;
using Updaemon.Configuration;
using Updaemon.Conversion;
using Updaemon.Interfaces;
using Updaemon.Services;

namespace Updaemon
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            // Setup cancellation token for Ctrl+C
            using (CancellationTokenSource cancellationTokenSource = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true; // Prevent immediate termination
                    cancellationTokenSource.Cancel();
                };

                // Setup DI container
                ServiceCollection services = new ServiceCollection();
                ConfigureServices(services);

                ServiceProvider serviceProvider = services.BuildServiceProvider();

                // Execute command
                CommandExecutor executor = serviceProvider.GetRequiredService<CommandExecutor>();
                int exitCode = await executor.ExecuteAsync(args, cancellationTokenSource.Token);

                // Cleanup
                if (serviceProvider is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    serviceProvider.Dispose();
                }

                return exitCode;
            }
        }

        private static void ConfigureServices(ServiceCollection services)
        {
            UpdaemonScope scope = UpdaemonScope.Detect();
            services.AddSingleton(scope);
            services.AddSingleton(scope.Platform);

            // Output writer
            services.AddSingleton<IOutputWriter, ConsoleOutputWriter>();
            services.AddSingleton<IRunLock, ScopeLock>();

            // Configuration and Secrets
            services.AddSingleton<IConfigManager, ConfigManager>();
            services.AddSingleton<ISecretsManager, SecretsManager>();

            // Service manager / timer manager: pick the OS-appropriate implementation.
            if (scope.Platform.IsMacOS)
            {
                services.AddSingleton<IServiceManager, MacServiceManager>();
                services.AddSingleton<ITimerManager, MacTimerManager>();
            }
            else
            {
                services.AddSingleton<IServiceManager, LinuxServiceManager>();
                services.AddSingleton<ITimerManager, LinuxTimerManager>();
            }

            // Other services
            services.AddSingleton<ISymlinkManager, SymlinkManager>();
            services.AddSingleton<IExecutableDetector, ExecutableDetector>();
            services.AddSingleton<IPrompter, ConsolePrompter>();
            services.AddSingleton<IUnitFileManager, UnitFileManager>();
            services.AddSingleton<IFilePermissionManager, FilePermissionManager>();
            services.AddSingleton<AppFileWriter>();
            services.AddSingleton<AppSettingsStore>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IServiceDeployer, ServiceDeployer>();
            services.AddSingleton<AppRollback>();
            services.AddTransient<PluginProvider>();

            services.AddSingleton(UpdaemonVersion.Current);
            services.AddSingleton(RunningExecutable.Current);
            services.AddSingleton<IUpdaemonReleases, GitHubUpdaemonReleases>();
            services.AddTransient<SelfUpdater>();
            services.AddSingleton<IGitHubPluginReleases, GitHubPluginReleases>();
            services.AddTransient<GitHubPluginUpdater>();

            // Distribution service client
            services.AddTransient<IDistributionServiceClient, DistributionServiceClient>();

            // HTTP client for downloading plugins, and updaemon's own builds
            services.AddSingleton<HttpClient>();
            services.AddSingleton<IReleaseChecksums, ReleaseChecksums>();

            // Plugin resolution, download and installation
            services.AddSingleton<IPluginUrlResolver, GitHubPluginUrlResolver>();
            services.AddTransient<IPluginDownloader, PluginDownloader>();
            services.AddTransient<IPluginInstaller, PluginInstaller>();

            // Commands - transient (created per invocation)
            services.AddTransient<InstallCommand>();
            services.AddTransient<UpdateCommand>();
            services.AddTransient<SetCommand>();
            services.AddTransient<UnsetCommand>();
            services.AddTransient<SetArgsCommand>();
            services.AddTransient<SetRestartCommand>();
            services.AddTransient<RemoveCommand>();
            services.AddTransient<RollbackCommand>();
            services.AddTransient<StatusCommand>();
            services.AddTransient<LogsCommand>();
            services.AddTransient<DistInstallCommand>();
            services.AddTransient<SelfUpdateCommand>();
            services.AddTransient<DistListCommand>();
            services.AddTransient<SecretSetCommand>();
            services.AddTransient<TimerCommand>();
            services.AddTransient<MigrateCommand>();
            services.AddTransient<IZeroXConverter, ZeroXConverter>();

            // Command infrastructure
            services.AddSingleton<ICommandFactory, CommandFactory>();
            services.AddSingleton<CommandExecutor>();
        }
    }
}
