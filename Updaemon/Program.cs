using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Updaemon.Commands;
using Updaemon.Configuration;
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
            // Output writer
            services.AddSingleton<IOutputWriter, ConsoleOutputWriter>();

            // Configuration and Secrets — parameterless constructors read from PlatformPaths.
            services.AddSingleton<IConfigManager, ConfigManager>();
            services.AddSingleton<ISecretsManager, SecretsManager>();

            // Service manager / timer manager: pick the OS-appropriate implementation.
            if (PlatformPaths.IsMacOS)
            {
                services.AddSingleton<IServiceManager, MacServiceManager>();
                services.AddSingleton<ITimerManager, MacTimerManager>();
            }
            else
            {
                services.AddSingleton<IServiceManager, LinuxServiceManager>();
                services.AddSingleton<ITimerManager, LinuxTimerManager>();
            }

            // Other services — parameterless constructors are platform-aware.
            services.AddSingleton<ISymlinkManager, SymlinkManager>();
            services.AddSingleton<IExecutableDetector, ExecutableDetector>();
            services.AddSingleton<IVersionExtractor, VersionExtractor>();
            services.AddSingleton<IUnitFileManager, UnitFileManager>();
            services.AddSingleton<IFilePermissionManager, FilePermissionManager>();
            services.AddSingleton<IServiceDeployer, ServiceDeployer>();

            // MERGE(19-redesign): placeholder. The platform type there owns the RID detector and the
            // running version; wire both from it and delete these two lines.
            string runtimeIdentifier = $"{(OperatingSystem.IsMacOS() ? "osx" : "linux")}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
            Version updaemonVersion = typeof(Program).Assembly.GetName().Version!;

            // Distribution service client
            services.AddTransient<IDistributionServiceClient>(_ => new DistributionServiceClient(runtimeIdentifier));

            // HTTP client for downloading plugins
            services.AddSingleton<HttpClient>();

            // Plugin resolution, download and installation
            services.AddSingleton<IPluginUrlResolver>(new GitHubPluginUrlResolver(GitHubPluginUrlResolver.LoadEmbeddedRegistry(), runtimeIdentifier));
            services.AddTransient<IPluginDownloader, PluginDownloader>();
            services.AddTransient<IPluginInstaller, PluginInstaller>();

            // Commands - transient (created per invocation)
            services.AddTransient<NewCommand>();
            services.AddTransient<InitCommand>();
            services.AddTransient<ListCommand>();
            services.AddTransient<UpdateCommand>();
            services.AddTransient<SetRemoteCommand>();
            services.AddTransient<SetExecNameCommand>();
            services.AddTransient<SetRestartCommand>();
            services.AddTransient(provider => new DistInstallCommand(
                provider.GetRequiredService<IOutputWriter>(),
                provider.GetRequiredService<IPluginInstaller>(),
                updaemonVersion));
            services.AddTransient(provider => new DistUpdateCommand(
                provider.GetRequiredService<IConfigManager>(),
                provider.GetRequiredService<IOutputWriter>(),
                provider.GetRequiredService<IPluginInstaller>(),
                provider.GetRequiredService<IPluginDownloader>(),
                updaemonVersion));
            services.AddTransient<DistListCommand>();
            services.AddTransient<SecretSetCommand>();
            services.AddTransient<TimerCommand>();

            // Command infrastructure
            services.AddSingleton<ICommandFactory, CommandFactory>();
            services.AddSingleton<CommandExecutor>();
        }
    }
}
