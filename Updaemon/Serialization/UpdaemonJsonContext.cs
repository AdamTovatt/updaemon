using System.Text.Json.Serialization;
using Updaemon.Models;

namespace Updaemon.Serialization
{
    /// <summary>
    /// JSON serialization context for updaemon internal models, enabling AOT compilation.
    /// For RPC types, use Updaemon.Common.Serialization.CommonJsonContext.
    /// </summary>
    [JsonSerializable(typeof(UpdaemonConfig))]
    [JsonSerializable(typeof(RegisteredService))]
    [JsonSerializable(typeof(InstalledPluginInfo))]
    [JsonSerializable(typeof(Dictionary<string, InstalledPluginInfo>))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(Dictionary<string, Dictionary<string, string>>))]
    [JsonSerializable(typeof(AppConfig))]
    [JsonSerializable(typeof(ServiceType))]
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    public partial class UpdaemonJsonContext : JsonSerializerContext
    {
    }
}

