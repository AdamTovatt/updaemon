using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of ISymlinkManager with in-memory symlinks.
    /// </summary>
    public class MockSymlinkManager : ISymlinkManager
    {
        public List<string> MethodCalls { get; } = new List<string>();
        public Dictionary<string, string> Symlinks { get; } = new Dictionary<string, string>();

        public Task CreateOrUpdateSymlinkAsync(string linkPath, string targetPath, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(CreateOrUpdateSymlinkAsync)}:{linkPath}:{targetPath}");
            Symlinks[linkPath] = targetPath;
            return Task.CompletedTask;
        }

        public Task<string?> ReadSymlinkAsync(string linkPath, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(ReadSymlinkAsync)}:{linkPath}");
            return Task.FromResult(Symlinks.GetValueOrDefault(linkPath));
        }

        public Task<bool> IsSymlinkAsync(string path, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(IsSymlinkAsync)}:{path}");
            return Task.FromResult(Symlinks.ContainsKey(path));
        }
    }
}

