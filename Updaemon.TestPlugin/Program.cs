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
            FixedVersionTestService service = new FixedVersionTestService();
            await DistributionServiceHost.RunAsync(args, service);
        }
    }

    internal class FixedVersionTestService : IDistributionService
    {
        public DistributionServiceInformation GetServiceInformation()
        {
            return new DistributionServiceInformation
            {
                FullName = "Test Plugin",
                DefaultAlias = "test",
                Description = "Returns a fixed version. Used by integration tests.",
                Version = "1.0.0",
                Secrets = new List<DistributionSecretInfo>(),
            };
        }

        public Task InitializeAsync(SecretCollection secrets, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// The remotes "fail" and "fail-silently" make the plugin fail, with and without a
        /// message, so tests can see how plugin errors reach updaemon.
        /// </summary>
        public Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            switch (serviceName)
            {
                case "fail":
                    throw new InvalidOperationException("Remote 'fail' does not exist. Check the remote name.");

                case "fail-silently":
                    throw new InvalidOperationException(string.Empty);

                default:
                    return Task.FromResult<Version?>(new Version(1, 0, 0));
            }
        }

        /// <summary>
        /// Writes one empty file named after the runtime identifier it was given, so tests can
        /// see which identifier crossed the pipe. The remote "unverified" reports an unverified
        /// download and "no-result" reports none, so tests can see how the result crosses it.
        /// </summary>
        public Task<DownloadResult> DownloadVersionAsync(string serviceName, Version version, string runtimeIdentifier, string targetPath, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(targetPath);
            File.WriteAllBytes(Path.Combine(targetPath, runtimeIdentifier), Array.Empty<byte>());
            return Task.FromResult(serviceName switch
            {
                "unverified" => new DownloadResult { Verified = false },
                "no-result" => null!,
                _ => new DownloadResult { Verified = true },
            });
        }
    }
}
