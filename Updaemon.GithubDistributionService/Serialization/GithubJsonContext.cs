using System.Text.Json.Serialization;
using Updaemon.GithubDistributionService.Models;

namespace Updaemon.GithubDistributionService.Serialization
{
    [JsonSerializable(typeof(GithubRelease))]
    [JsonSerializable(typeof(GithubAsset))]
    [JsonSerializable(typeof(GithubAsset[]))]
    internal partial class GithubJsonContext : JsonSerializerContext
    {
    }
}

