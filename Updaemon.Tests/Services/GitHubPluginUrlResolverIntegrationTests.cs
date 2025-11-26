using Updaemon.Services;

namespace Updaemon.Tests.Services
{
    [Trait("Category", "Integration")]
    public class GitHubPluginUrlResolverIntegrationTests
    {
        [Fact]
        public async Task ResolveAsync_ResolvesGithubFromRealRegistry()
        {
            HttpClient httpClient = new HttpClient();
            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

            string url = await resolver.ResolveAsync("github");

            Assert.NotNull(url);
            Assert.StartsWith("https://", url);
            Assert.Contains("Updaemon.GithubDistributionService", url);
        }

        [Fact]
        public async Task ResolveAsync_ResolvesByteshelfFromRealRegistry()
        {
            HttpClient httpClient = new HttpClient();
            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

            string url = await resolver.ResolveAsync("byteshelf");

            Assert.NotNull(url);
            Assert.StartsWith("https://", url);
            Assert.Contains("Updaemon.Distribution.ByteShelfDistribution", url);
        }

        [Fact]
        public async Task ResolveAsync_NonexistentPlugin_ThrowsException()
        {
            HttpClient httpClient = new HttpClient();
            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("nonexistent-plugin-name"));

            Assert.Contains("not found in the registry", exception.Message);
            Assert.Contains("full URL", exception.Message);
        }
    }
}

