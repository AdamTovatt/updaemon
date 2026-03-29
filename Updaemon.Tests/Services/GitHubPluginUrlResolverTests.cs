using System.Net;
using System.Text;
using System.Text.Json;
using Updaemon.Services;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    public class GitHubPluginUrlResolverTests
    {
        [Fact]
        public async Task ResolveAsync_ValidPluginName_ReturnsUrl()
        {
            Dictionary<string, string> registry = new Dictionary<string, string>
            {
                { "github", "https://example.com/github-plugin" },
                { "byteshelf", "https://example.com/byteshelf-plugin" }
            };
            string jsonContent = JsonSerializer.Serialize(registry);
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetResponse(Encoding.UTF8.GetBytes(jsonContent));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

            string url = await resolver.ResolveAsync("github");

            Assert.Equal("https://example.com/github-plugin", url);
        }

        [Fact]
        public async Task ResolveAsync_PluginNameNotFound_ThrowsInvalidOperationException()
        {
            Dictionary<string, string> registry = new Dictionary<string, string>
            {
                { "github", "https://example.com/github-plugin" }
            };
            string jsonContent = JsonSerializer.Serialize(registry);
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetResponse(Encoding.UTF8.GetBytes(jsonContent));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("nonexistent"));

            Assert.Contains("not found in the registry", exception.Message);
            Assert.Contains("full URL", exception.Message);
        }

        [Fact]
        public async Task ResolveAsync_EmptyPluginName_ThrowsArgumentException()
        {
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            HttpClient httpClient = new HttpClient(mockHandler);
            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

            await Assert.ThrowsAsync<ArgumentException>(
                () => resolver.ResolveAsync(""));
        }

        [Fact]
        public async Task ResolveAsync_WhitespacePluginName_ThrowsArgumentException()
        {
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            HttpClient httpClient = new HttpClient(mockHandler);
            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

            await Assert.ThrowsAsync<ArgumentException>(
                () => resolver.ResolveAsync("   "));
        }

        [Fact]
        public async Task ResolveAsync_HttpRequestException_ThrowsInvalidOperationException()
        {
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetException(new HttpRequestException("Network error"));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

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

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

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

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

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

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("github"));

            Assert.Contains("not found in the registry", exception.Message);
        }

        [Fact]
        public async Task ResolveAsync_PluginWithEmptyUrl_ThrowsInvalidOperationException()
        {
            Dictionary<string, string> registry = new Dictionary<string, string>
            {
                { "github", "" },
                { "byteshelf", "https://example.com/byteshelf-plugin" }
            };
            string jsonContent = JsonSerializer.Serialize(registry);
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetResponse(Encoding.UTF8.GetBytes(jsonContent));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("github"));

            Assert.Contains("not found in the registry", exception.Message);
        }

        [Fact]
        public async Task ResolveAsync_HttpNotFound_ThrowsInvalidOperationException()
        {
            MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
            mockHandler.SetException(new HttpRequestException("Not Found", null, HttpStatusCode.NotFound));
            HttpClient httpClient = new HttpClient(mockHandler);

            GitHubPluginUrlResolver resolver = new GitHubPluginUrlResolver(httpClient);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.ResolveAsync("github"));

            Assert.Contains("Failed to fetch plugin registry", exception.Message);
        }
    }
}

