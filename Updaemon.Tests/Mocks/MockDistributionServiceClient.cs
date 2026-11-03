using Updaemon.Common.Models;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IDistributionServiceClient with configurable responses.
    /// </summary>
    public class MockDistributionServiceClient : IDistributionServiceClient
    {
        public List<string> MethodCalls { get; } = new List<string>();
        public Dictionary<string, Version?> LatestVersions { get; } = new Dictionary<string, Version?>();
        public List<(string ServiceName, Version Version, string TargetPath)> Downloads { get; } = new List<(string, Version, string)>();
        public string? ConnectedPluginPath { get; private set; }
        public string? InitializedSecrets { get; private set; }
        public bool IsDisposed { get; private set; }
        public DistributionServiceInformation? CustomServiceInformation { get; set; }
        public HashSet<string> ConnectAsyncThrowsForPluginPaths { get; } = new HashSet<string>();

        public Task<DistributionServiceInformation> ConnectAsync(string pluginExecutablePath, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(ConnectAsync)}:{pluginExecutablePath}");
            if (ConnectAsyncThrowsForPluginPaths.Contains(pluginExecutablePath))
            {
                throw new InvalidOperationException($"Mock failure connecting to '{pluginExecutablePath}'");
            }

            ConnectedPluginPath = pluginExecutablePath;
            return Task.FromResult(CustomServiceInformation ?? new DistributionServiceInformation
            {
                FullName = "Mock Distribution Service",
                DefaultAlias = "mock",
                Description = "Mock distribution service for testing",
                Version = "1.0.0",
                Secrets = new List<DistributionSecretInfo>()
            });
        }

        public Task InitializeAsync(string? secrets, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(InitializeAsync)}:{secrets ?? "(null)"}");
            InitializedSecrets = secrets;
            return Task.CompletedTask;
        }

        /// <summary>Remotes the plugin refuses, with the message it gives.</summary>
        public Dictionary<string, string> RefusedRemotes { get; } = new Dictionary<string, string>();

        public Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(GetLatestVersionAsync)}:{serviceName}");
            if (RefusedRemotes.TryGetValue(serviceName, out string? message))
            {
                throw new PluginException(message);
            }

            return Task.FromResult(LatestVersions.GetValueOrDefault(serviceName));
        }

        /// <summary>Whether downloads report they were verified against a checksum.</summary>
        public bool DownloadsVerified { get; set; } = true;

        public Task<DownloadResult> DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(DownloadVersionAsync)}:{serviceName}:{version}:{targetPath}");
            Downloads.Add((serviceName, version, targetPath));
            if (ReleaseWriters.TryGetValue($"{serviceName}:{version}", out Action<string>? writeRelease))
            {
                writeRelease(targetPath);
            }

            return Task.FromResult(new DownloadResult { Verified = DownloadsVerified });
        }

        /// <summary>Writes a release's files into the download directory, by "remote:version".</summary>
        public Dictionary<string, Action<string>> ReleaseWriters { get; } = new Dictionary<string, Action<string>>();

        /// <summary>Publishes a release whose files <paramref name="writeRelease"/> creates; it becomes the latest version.</summary>
        public void AddRelease(string serviceName, Version version, Action<string> writeRelease)
        {
            ReleaseWriters[$"{serviceName}:{version}"] = writeRelease;
            LatestVersions[serviceName] = version;
        }

        public ValueTask DisposeAsync()
        {
            MethodCalls.Add(nameof(DisposeAsync));
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }

        public void SetLatestVersion(string serviceName, Version? version)
        {
            LatestVersions[serviceName] = version;
        }
    }
}

