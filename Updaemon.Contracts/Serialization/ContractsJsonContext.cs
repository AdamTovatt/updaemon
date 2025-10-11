using System.Text.Json.Serialization;
using Updaemon.Contracts.Rpc;

namespace Updaemon.Contracts.Serialization
{
    /// <summary>
    /// JSON serialization context for updaemon contracts, enabling AOT compilation.
    /// Both updaemon and distribution plugins should use this context for RPC communication.
    /// </summary>
    [JsonSerializable(typeof(RpcRequest))]
    [JsonSerializable(typeof(RpcResponse))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(object))]
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    public partial class ContractsJsonContext : JsonSerializerContext
    {
    }
}

