using System.Text.Json.Serialization;
using Updaemon.Models;

namespace Updaemon.Serialization
{
    /// <summary>
    /// JSON serialization context for updaemon internal models, enabling AOT compilation.
    /// For RPC types, use Updaemon.Contracts.Serialization.ContractsJsonContext.
    /// </summary>
    [JsonSerializable(typeof(UpdaemonConfig))]
    [JsonSerializable(typeof(RegisteredService))]
    [JsonSerializable(typeof(AppConfig))]
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    public partial class UpdaemonJsonContext : JsonSerializerContext
    {
    }
}

