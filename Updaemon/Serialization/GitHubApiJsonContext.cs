using System.Text.Json.Serialization;
using Updaemon.Models;

namespace Updaemon.Serialization
{
    /// <summary>The GitHub API's JSON, which names properties in snake_case.</summary>
    [JsonSerializable(typeof(GitHubRelease))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
    public partial class GitHubApiJsonContext : JsonSerializerContext
    {
    }
}
