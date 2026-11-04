using Updaemon.Common.Models;

namespace Updaemon.Common.Rpc
{
    /// <summary>
    /// Represents an RPC response message sent from a distribution plugin to updaemon.
    /// </summary>
    public class RpcResponse
    {
        /// <summary>
        /// Unique identifier matching the request.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// JSON-serialized result of the method invocation.
        /// </summary>
        public string? Result { get; set; }

        /// <summary>
        /// Error message if the invocation failed.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>Set beside <see cref="Error"/> when the call failed for want of a secret.</summary>
        public CredentialNeed? CredentialNeeded { get; set; }

        /// <summary>
        /// Indicates if the invocation was successful.
        /// </summary>
        public bool Success { get; set; }
    }
}

