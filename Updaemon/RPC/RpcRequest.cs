namespace Updaemon.RPC
{
    /// <summary>
    /// Represents an RPC request message.
    /// </summary>
    public class RpcRequest
    {
        /// <summary>
        /// Unique identifier for the request.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The method name to invoke.
        /// </summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>
        /// JSON-serialized parameters for the method.
        /// </summary>
        public string? Parameters { get; set; }
    }
}

