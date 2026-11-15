using ByteShelfClient;
using ByteShelfClient.Http;

namespace Updaemon.Distribution.ByteShelfDistribution
{
    /// <summary>
    /// Connects to a ByteShelf server over HTTP with a shelf key. Every client shares one handler, so the
    /// client made for a key given later, after a prompt, reuses its connections and nothing is left open.
    /// </summary>
    public sealed class ByteShelfHttpConnector : IDisposable
    {
        private readonly HttpMessageHandler _handler;

        public ByteShelfHttpConnector(HttpMessageHandler? handler = null)
        {
            _handler = handler ?? new SocketsHttpHandler();
        }

        /// <param name="server">The server's address, ending in '/'.</param>
        /// <param name="key">A shelf key's secret.</param>
        public IByteShelfClient Connect(Uri server, string key)
        {
            // The HttpClient holds nothing of its own: the shared handler holds the connections.
            HttpClient http = new HttpClient(_handler, disposeHandler: false) { BaseAddress = server };
            return new ByteShelfHttpClient(http, new ShelfKeyCredentials(key));
        }

        public void Dispose()
        {
            _handler.Dispose();
        }
    }
}
