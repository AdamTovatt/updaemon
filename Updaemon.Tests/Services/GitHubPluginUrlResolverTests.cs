using System.Net;
using System.Text;
using System.Text.Json;
using Updaemon.Services;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    public class GitHubPluginUrlResolverTests
    {
        private const string TestRid = "linux-arm64";

        private static string SerializeRegistry(Dictionary<string, Dictionary<string, string>> registry)
        {
            return JsonSerializer.Serialize(registry);
        }

        [Fact]
        public async Task ResolveAsync_ValidPluginNameAndRid_ReturnsUrl()
        {
            Dictionary<string, Dictionary<string, string>> registry = new()
            {
                ["github"] = new() { [TestRid] = "https://example.com/github-plugin" },
                ["byteshelf"] = new() { [TestRid] = "https://example.com/byteshelf-plugin" },
            };
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetResponse(Encoding.UTF8.GetBytes(SerializeRegistry(registry)));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient, TestRid);

            string url = await resolver.ResolveAsync("github");

            Assert.Equal("https://example.com/github-plugin", url);
        }

        [Fact]
        public async Task ResolveAsync_PluginNameNotFound_ThrowsInvalidOperationException()
        {
            Dictionary<string, Dictionary<string, string>> registry = new()
            {
                ["github"] = new() { [TestRid] = "https://example.com/github-plugin" },
            };
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetResponse(Encoding.UTF8.GetBytes(SerializeRegistry(registry)));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient, TestRid);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("nonexistent"));

            Assert.Contains("not found in the registry", exception.Message);
            Assert.Contains("full URL", exception.Message);
        }

        [Fact]
        public async Task ResolveAsync_PluginExistsButNoBuildForRid_ThrowsWithAvailableRids()
        {
            Dictionary<string, Dictionary<string, string>> registry = new()
            {
                ["github"] = new() { ["linux-arm64"] = "https://example.com/linux-build" },
            };
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetResponse(Encoding.UTF8.GetBytes(SerializeRegistry(registry)));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient, "osx-arm64");

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("github"));

            Assert.Contains("no build for runtime 'osx-arm64'", exception.Message);
            Assert.Contains("linux-arm64", exception.Message);
        }

        [Fact]
        public async Task ResolveAsync_EmptyPluginName_ThrowsArgumentException()
        {
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            HttpClient httpClient = new HttpClient(mockHandler);
            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient, TestRid);

            await Assert.ThrowsAsync<ArgumentException>(
                () => resolver.ResolveAsync(""));
        }

        [Fact]
        public async Task ResolveAsync_WhitespacePluginName_ThrowsArgumentException()
        {
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            HttpClient httpClient = new HttpClient(mockHandler);
            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient, TestRid);

            await Assert.ThrowsAsync<ArgumentException>(
                () => resolver.ResolveAsync("   "));
        }

        [Fact]
        public async Task ResolveAsync_HttpRequestException_ThrowsInvalidOperationException()
        {
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetException(new HttpRequestException("Network error"));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient, TestRid);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("github"));

            Assert.Contains("Failed to fetch plugin registry", exception.Message);
            Assert.Contains("full URL", exception.Message);
            Assert.NotNull(exception.InnerException);
        }

        [Fact]
        public async Task ResolveAsync_InvalidJson_ThrowsInvalidOperationException()
        {
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetResponse(Encoding.UTF8.GetBytes("invalid json {{{"));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient, TestRid);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("github"));

            Assert.Contains("Failed to parse plugin registry", exception.Message);
            Assert.Contains("full URL", exception.Message);
        }

        [Fact]
        public async Task ResolveAsync_NullRegistry_ThrowsInvalidOperationException()
        {
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetResponse(Encoding.UTF8.GetBytes("null"));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient, TestRid);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("github"));

            Assert.Contains("Failed to parse plugin registry", exception.Message);
            Assert.Contains("full URL", exception.Message);
        }

        [Fact]
        public async Task ResolveAsync_EmptyRegistry_ThrowsInvalidOperationException()
        {
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetResponse(Encoding.UTF8.GetBytes("{}"));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient, TestRid);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("github"));

            Assert.Contains("not found in the registry", exception.Message);
        }

        [Fact]
        public async Task ResolveAsync_PluginWithEmptyUrl_ThrowsInvalidOperationException()
        {
            Dictionary<string, Dictionary<string, string>> registry = new()
            {
                ["github"] = new() { [TestRid] = "" },
                ["byteshelf"] = new() { [TestRid] = "https://example.com/byteshelf-plugin" },
            };
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetResponse(Encoding.UTF8.GetBytes(SerializeRegistry(registry)));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient, TestRid);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("github"));

            Assert.Contains("no build for runtime", exception.Message);
        }

        [Fact]
        public async Task ResolveAsync_HttpNotFound_ThrowsInvalidOperationException()
        {
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetException(new HttpRequestException("Not Found", null, HttpStatusCode.NotFound));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient, TestRid);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("github"));

            Assert.Contains("Failed to fetch plugin registry", exception.Message);
        }
    }
}
