using System.Text.Json.Serialization;
using ByteShelfCommon;

namespace Updaemon.Distribution.ByteShelfDistribution.Serialization
{
    /// <summary>
    /// JSON serialization context for AOT compilation.
    /// Defines all types that need to be serialized/deserialized.
    /// </summary>
    [JsonSerializable(typeof(ShelfFileMetadata))]
    [JsonSerializable(typeof(ShelfFileMetadata[]))]
    [JsonSerializable(typeof(IEnumerable<ShelfFileMetadata>))]
    [JsonSerializable(typeof(TenantInfoResponse))]
    [JsonSerializable(typeof(Dictionary<string, TenantInfoResponse>))]
    internal partial class ByteShelfJsonContext : JsonSerializerContext
    {
    }
}

