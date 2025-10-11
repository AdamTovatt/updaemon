namespace Updaemon.Common.Rpc
{
    /// <summary>
    /// Represents an RPC request message sent from updaemon to a distribution plugin.
    /// </summary>
    public class RpcRequest
    {
        /// <summary>
        /// Unique identifier for the request.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The method name to invoke (e.g., "InitializeAsync", "GetLatestVersionAsync").
        /// </summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>
        /// JSON-serialized parameters for the method.
        /// </summary>
        public string? Parameters { get; set; }
    }
}

