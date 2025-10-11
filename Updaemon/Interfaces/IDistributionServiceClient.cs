using Updaemon.Common;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Client for communicating with distribution service plugins via named pipes.
    /// </summary>
    public interface IDistributionServiceClient : IDistributionService, IAsyncDisposable
    {
        /// <summary>
        /// Connects to the distribution service plugin executable.
        /// </summary>
        /// <param name="pluginExecutablePath">Path to the plugin executable.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        Task ConnectAsync(string pluginExecutablePath, CancellationToken cancellationToken = default);
    }
}

