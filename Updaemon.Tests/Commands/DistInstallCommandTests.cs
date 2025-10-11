using System.Net;
using Updaemon.Commands;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class DistInstallCommandTests
    {
        [Fact]
        public async Task ExecuteAsync_UpdatesConfigWithPluginPath()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
                mockHandler.SetResponse(new byte[] { 0x7F, 0x45, 0x4C, 0x46 }); // ELF header bytes
                HttpClient httpClient = new HttpClient(mockHandler);
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");
                
                DistInstallCommand command = new DistInstallCommand(configManager, httpClient, pluginsDirectory);

                await command.ExecuteAsync("https://example.com/plugins/my-plugin");

                Assert.Contains(configManager.MethodCalls, call => call.StartsWith("SetDistributionPluginPathAsync:"));
                string? pluginPath = await configManager.GetDistributionPluginPathAsync();
                Assert.NotNull(pluginPath);
                Assert.Contains("my-plugin", pluginPath);
            }
        }

        [Fact]
        public async Task ExecuteAsync_HandlesDownloadFailure()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
                mockHandler.SetException(new HttpRequestException("Network error"));
                HttpClient httpClient = new HttpClient(mockHandler);
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");
                
                DistInstallCommand command = new DistInstallCommand(configManager, httpClient, pluginsDirectory);

                await Assert.ThrowsAsync<HttpRequestException>(
                    async () => await command.ExecuteAsync("https://example.com/invalid-plugin")
                );
            }
        }

        [Fact]
        public async Task ExecuteAsync_ExtractsFilenameFromUrl()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
                mockHandler.SetResponse(new byte[] { 0x7F, 0x45, 0x4C, 0x46 });
                HttpClient httpClient = new HttpClient(mockHandler);
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");
                
                DistInstallCommand command = new DistInstallCommand(configManager, httpClient, pluginsDirectory);

                await command.ExecuteAsync("https://example.com/path/to/byteshelf-dist");

                string? pluginPath = await configManager.GetDistributionPluginPathAsync();
                Assert.NotNull(pluginPath);
                Assert.Contains("byteshelf-dist", pluginPath);
            }
        }
    }

    /// <summary>
    /// Mock HttpMessageHandler for testing HTTP requests without network calls.
    /// </summary>
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private byte[]? _response;
        private Exception? _exception;

        public void SetResponse(byte[] response)
        {
            _response = response;
            _exception = null;
        }

        public void SetException(Exception exception)
        {
            _exception = exception;
            _response = null;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_exception != null)
            {
                throw _exception;
            }

            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_response ?? Array.Empty<byte>()),
            };

            return Task.FromResult(response);
        }
    }
}

