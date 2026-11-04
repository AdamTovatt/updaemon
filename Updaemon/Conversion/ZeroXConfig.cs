using System.Text.Json;
using System.Text.Json.Serialization;

namespace Updaemon.Conversion
{
    /// <summary>
    /// An updaemon 0.x <c>config.json</c>: the system scope's, with no <c>format</c> field. Read only
    /// to convert it.
    /// </summary>
    public sealed class ZeroXConfig
    {
        public Dictionary<string, ZeroXPlugin> InstalledPlugins { get; set; } = new Dictionary<string, ZeroXPlugin>();

        public List<ZeroXApp> Services { get; set; } = new List<ZeroXApp>();

        public int ReleaseRetentionCount { get; set; } = 5;

        /// <summary>
        /// Whether <paramref name="json"/> is a 0.x config: an object without a <c>format</c> field.
        /// The one rule for it; text that is no JSON object is no config of either kind.
        /// </summary>
        public static bool IsZeroX(string json)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    return document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty("format", out _);
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static ZeroXConfig Parse(string json) =>
            JsonSerializer.Deserialize(json, ZeroXJsonContext.Default.ZeroXConfig) ?? new ZeroXConfig();
    }

    public sealed class ZeroXPlugin
    {
        public string Alias { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;
    }

    /// <summary>A 0.x service or CLI tool.</summary>
    public sealed class ZeroXApp
    {
        public string LocalName { get; set; } = string.Empty;

        public string RemoteName { get; set; } = string.Empty;

        /// <summary>The name its executable was searched for by; null meant <see cref="LocalName"/>.</summary>
        public string? ExecutableName { get; set; }

        public string DistributionPluginAlias { get; set; } = string.Empty;

        public ZeroXAppType ServiceType { get; set; } = ZeroXAppType.Service;

        public bool AutoRestart { get; set; } = true;
    }

    [JsonConverter(typeof(JsonStringEnumConverter<ZeroXAppType>))]
    public enum ZeroXAppType
    {
        Service = 0,
        Cli = 1,
    }

    [JsonSerializable(typeof(ZeroXConfig))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal partial class ZeroXJsonContext : JsonSerializerContext
    {
    }
}
