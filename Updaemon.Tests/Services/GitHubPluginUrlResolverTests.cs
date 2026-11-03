using Updaemon.Services;

namespace Updaemon.Tests.Services
{
    public class GitHubPluginUrlResolverTests
    {
        [Theory]
        [InlineData("github", "1.2", "https://github.com/AdamTovatt/updaemon/releases/download/v1.2.0/Updaemon.GithubDistributionService-linux-x64")]
        [InlineData("github", "0.11.0.0", "https://github.com/AdamTovatt/updaemon/releases/download/v0.11.0/Updaemon.GithubDistributionService-linux-x64")]
        [InlineData("gitlab", "1.2.0", null)]
        public void Resolve_BuildsTheReleaseAssetUrlOrReturnsNullForAnUnknownName(string pluginName, string updaemonVersion, string? expectedUrl)
        {
            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(
                new Dictionary<string, string> { ["github"] = "Updaemon.GithubDistributionService" },
                "linux-x64");

            Assert.Equal(expectedUrl, resolver.Resolve(pluginName, Version.Parse(updaemonVersion)));
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
