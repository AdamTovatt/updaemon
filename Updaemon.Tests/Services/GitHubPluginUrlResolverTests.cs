using Updaemon.Configuration;
using Updaemon.Services;

namespace Updaemon.Tests.Services
{
    public class GitHubPluginUrlResolverTests
    {
        [Theory]
        [InlineData("github", "https://github.com/AdamTovatt/updaemon/releases/download/v1.2.0/Updaemon.GithubDistributionService-linux-x64")]
        [InlineData("gitlab", null)]
        public void Resolve_BuildsTheReleaseAssetUrlOrReturnsNullForAnUnknownName(string pluginName, string? expectedUrl)
        {
            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(
                new Dictionary<string, string> { ["github"] = "Updaemon.GithubDistributionService" },
                new HostPlatform(false, "linux-x64"));

            Assert.Equal(expectedUrl, resolver.Resolve(pluginName, UpdaemonVersion.Of(1, 2, 0)));
        }

        [Theory]
        [InlineData("Updaemon.GithubDistributionService-linux-arm64", "github")]
        [InlineData("Updaemon.GithubDistributionService", "github")]
        [InlineData("Updaemon.GithubDistributionServiceX", null)]
        [InlineData("my-own-plugin", null)]
        public void FindPluginName_GoesByTheAssetBaseNameWithOrWithoutAPlatform(string fileName, string? expected)
        {
            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(
                new Dictionary<string, string> { ["github"] = "Updaemon.GithubDistributionService" },
                new HostPlatform(false, "linux-x64"));

            Assert.Equal(expected, resolver.FindPluginName(fileName));
        }

        [Fact]
        public void LoadEmbeddedRegistry_MapsEachFirstPartyPluginToItsReleaseAssetBaseName()
        {
            IReadOnlyDictionary<string, string> registry = GitHubPluginUrlResolver.LoadEmbeddedRegistry();

            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["github"] = "Updaemon.GithubDistributionService",
                    ["byteshelf"] = "Updaemon.Distribution.ByteShelfDistribution",
                },
                registry);
        }
    }
}
