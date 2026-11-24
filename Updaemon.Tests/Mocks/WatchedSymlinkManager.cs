using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>Real links, with a call after each is made.</summary>
    public sealed class WatchedSymlinkManager : ISymlinkManager
    {
        private readonly ISymlinkManager _inner;

        public WatchedSymlinkManager(ISymlinkManager inner)
        {
            _inner = inner;
        }

        public Action? OnLinked { get; set; }

        public async Task CreateOrUpdateSymlinkAsync(string linkPath, string targetPath, string? temporaryDirectory = null, CancellationToken cancellationToken = default)
        {
            await _inner.CreateOrUpdateSymlinkAsync(linkPath, targetPath, temporaryDirectory, cancellationToken);
            OnLinked?.Invoke();
        }

        public Task<string?> ReadSymlinkAsync(string linkPath, CancellationToken cancellationToken = default) => _inner.ReadSymlinkAsync(linkPath, cancellationToken);
    }
}
