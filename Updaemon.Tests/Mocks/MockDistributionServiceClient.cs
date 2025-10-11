using Updaemon.Interfaces;

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

        public Task ConnectAsync(string pluginExecutablePath, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(ConnectAsync)}:{pluginExecutablePath}");
            ConnectedPluginPath = pluginExecutablePath;
            return Task.CompletedTask;
        }

        public Task InitializeAsync(string? secrets, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(InitializeAsync)}:{secrets ?? "(null)"}");
            InitializedSecrets = secrets;
            return Task.CompletedTask;
        }

        public Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(GetLatestVersionAsync)}:{serviceName}");
            return Task.FromResult(LatestVersions.GetValueOrDefault(serviceName));
        }

        public Task DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(DownloadVersionAsync)}:{serviceName}:{version}:{targetPath}");
            Downloads.Add((serviceName, version, targetPath));
            return Task.CompletedTask;
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

