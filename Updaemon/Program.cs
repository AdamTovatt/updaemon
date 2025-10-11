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
            // Setup DI container
            ServiceCollection services = new ServiceCollection();
            ConfigureServices(services);

            ServiceProvider serviceProvider = services.BuildServiceProvider();

            // Execute command
            CommandExecutor executor = serviceProvider.GetRequiredService<CommandExecutor>();
            int exitCode = await executor.ExecuteAsync(args);

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

        private static void ConfigureServices(ServiceCollection services)
        {
            // Output writer
            services.AddSingleton<IOutputWriter, ConsoleOutputWriter>();

            // Configuration and Secrets
            services.AddSingleton<IConfigManager, ConfigManager>();
            services.AddSingleton<ISecretsManager, SecretsManager>();

            // Service utilities
            services.AddSingleton<IServiceManager, ServiceManager>();
            services.AddSingleton<ISymlinkManager, SymlinkManager>();
            services.AddSingleton<IExecutableDetector, ExecutableDetector>();

            // Distribution service client
            services.AddTransient<IDistributionServiceClient, DistributionServiceClient>();

            // HTTP client for downloading plugins
            services.AddSingleton<HttpClient>();

            // Commands
            services.AddSingleton<NewCommand>();
            services.AddSingleton<UpdateCommand>();
            services.AddSingleton<SetRemoteCommand>();
            services.AddSingleton<DistInstallCommand>();
            services.AddSingleton<SecretSetCommand>();

            // Command executor
            services.AddSingleton<CommandExecutor>();
        }
    }
}