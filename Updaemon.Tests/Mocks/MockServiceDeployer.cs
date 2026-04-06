using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Tests.Mocks
{
    public class MockServiceDeployer : IServiceDeployer
    {
        public List<string> MethodCalls { get; } = new List<string>();
        public string ServiceBaseDirectory { get; set; } = "/opt";

        /// <summary>
        /// Maps localName to the current symlink target. If absent, ReadCurrentTargetAsync returns null.
        /// </summary>
        public Dictionary<string, string> CurrentTargets { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Maps "localName:version" to the deploy result. If absent, DeployVersionAsync returns null.
        /// </summary>
        public Dictionary<string, DeployResult> DeployResults { get; } = new Dictionary<string, DeployResult>();

        public List<DeployResult> CleanedUpDeploys { get; } = new List<DeployResult>();

        /// <summary>
        /// Tracks calls to PruneOldVersionsAsync as (localName, retentionCount) tuples.
        /// </summary>
        public List<(string LocalName, int RetentionCount)> PrunedServices { get; } = new List<(string, int)>();

        public string GetSymlinkPath(string localName)
        {
            return Path.Combine(ServiceBaseDirectory, localName, "current");
        }

        public Task<string?> ReadCurrentTargetAsync(string localName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"ReadCurrentTargetAsync:{localName}");
            CurrentTargets.TryGetValue(localName, out string? target);
            return Task.FromResult(target);
        }

        public Task<DeployResult?> DeployVersionAsync(
            RegisteredService service,
            Version version,
            IDistributionServiceClient distributionClient,
            CancellationToken cancellationToken = default)
        {
            string key = $"{service.LocalName}:{version}";
            MethodCalls.Add($"DeployVersionAsync:{key}");
            DeployResults.TryGetValue(key, out DeployResult? result);
            return Task.FromResult(result);
        }

        public Task PruneOldVersionsAsync(string localName, int retentionCount, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"PruneOldVersionsAsync:{localName}:{retentionCount}");
            PrunedServices.Add((localName, retentionCount));
            return Task.CompletedTask;
        }

        public Task CleanupDeployAsync(DeployResult result, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"CleanupDeployAsync:{result.VersionDirectory}");
            CleanedUpDeploys.Add(result);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Helper to set up a service as initialized with a given current target.
        /// </summary>
        public void SetInitialized(string localName, string currentTarget)
        {
            CurrentTargets[localName] = currentTarget;
        }

        /// <summary>
        /// Helper to configure a successful deploy result for a service + version.
        /// </summary>
        public DeployResult SetDeployResult(string localName, Version version, string? executableName = null)
        {
            string versionDir = Path.Combine(ServiceBaseDirectory, localName, version.ToString());
            string execName = executableName ?? localName;
            DeployResult result = new DeployResult
            {
                VersionDirectory = versionDir,
                ExecutablePath = Path.Combine(versionDir, execName),
                SymlinkPath = GetSymlinkPath(localName),
            };
            DeployResults[$"{localName}:{version}"] = result;
            return result;
        }
    }
}
