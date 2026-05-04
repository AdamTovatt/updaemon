using Updaemon.Common;
using Updaemon.Common.Hosting;
using Updaemon.Common.Models;

namespace Updaemon.TestPlugin
{
    /// <summary>
    /// Minimal distribution service plugin used by integration tests that need a real
    /// child process to spawn via Process.Start. Returns deterministic, in-memory data
    /// so tests can verify the host-to-plugin pipe lifecycle without hitting any network.
    /// </summary>
    internal class Program
    {
        static async Task Main(string[] args)
        {
            NoOpDistributionService service = new NoOpDistributionService();
            await DistributionServiceHost.RunAsync(args, service);
        }
    }

    internal class NoOpDistributionService : IDistributionService
    {
        public DistributionServiceInformation GetServiceInformation()
        {
            return new DistributionServiceInformation
            {
                FullName = "Test Plugin",
                DefaultAlias = "test",
                Description = "No-op plugin for integration tests.",
                Version = "1.0.0",
                Secrets = new List<DistributionSecretInfo>(),
            };
        }

        public Task InitializeAsync(SecretCollection secrets, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Version?>(new Version(1, 0, 0));
        }

        public Task DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
