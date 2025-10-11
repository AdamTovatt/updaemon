using System.Text.Json.Serialization;
using Updaemon.Models;
using Updaemon.RPC;

namespace Updaemon.Serialization
{
    /// <summary>
    /// JSON serialization context for AOT compilation.
    /// </summary>
    [JsonSerializable(typeof(UpdaemonConfig))]
    [JsonSerializable(typeof(RegisteredService))]
    [JsonSerializable(typeof(AppConfig))]
    [JsonSerializable(typeof(RpcRequest))]
    [JsonSerializable(typeof(RpcResponse))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(object))]
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    public partial class UpdaemonJsonContext : JsonSerializerContext
    {
    }
}

