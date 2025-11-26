using System.Net;
using Updaemon.Commands;
using Updaemon.Models;
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

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                DistInstallCommand command = new DistInstallCommand(configManager, httpClient, new MockOutputWriter(), distributionClient, new MockPluginUrlResolver(), pluginsDirectory);

                await command.ExecuteAsync(null, "https://example.com/plugins/my-plugin");

                Assert.Contains(configManager.MethodCalls, call => call.StartsWith("AddOrUpdatePluginAsync:"));
                IReadOnlyDictionary<string, InstalledPluginInfo> plugins = await configManager.GetAllPluginsAsync();
                Assert.NotEmpty(plugins);
                Assert.Contains("my-plugin", plugins.Values.First().Path);
            }
        }

        [Fact]
        public async Task ExecuteAsync_UsesProvidedAlias_WhenAsSpecified()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
                mockHandler.SetResponse(new byte[] { 0x7F, 0x45, 0x4C, 0x46 });
                HttpClient httpClient = new HttpClient(mockHandler);
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                DistInstallCommand command = new DistInstallCommand(configManager, httpClient, new MockOutputWriter(), distributionClient, new MockPluginUrlResolver(), pluginsDirectory);

                await command.ExecuteAsync("github", "https://example.com/path/to/plugin-bin");

                IReadOnlyDictionary<string, InstalledPluginInfo> plugins = await configManager.GetAllPluginsAsync();
                Assert.True(plugins.ContainsKey("github"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_UsesDefaultAlias_WhenAliasNotProvided()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
                mockHandler.SetResponse(new byte[] { 0x7F, 0x45, 0x4C, 0x46 });
                HttpClient httpClient = new HttpClient(mockHandler);
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");

                // MockDistributionServiceClient returns DefaultAlias = "mock"
                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                DistInstallCommand command = new DistInstallCommand(configManager, httpClient, new MockOutputWriter(), distributionClient, new MockPluginUrlResolver(), pluginsDirectory);

                await command.ExecuteAsync(null, "https://example.com/path/to/plugin-bin");

                IReadOnlyDictionary<string, InstalledPluginInfo> plugins = await configManager.GetAllPluginsAsync();
                Assert.True(plugins.ContainsKey("mock"));
            }
        }

        [Fact]
        public async Task ExecuteAsync_Throws_WhenAliasAlreadyExists()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                // Pre-register alias
                await configManager.AddOrUpdatePluginAsync(new InstalledPluginInfo { Alias = "dup", Path = "/existing/path" });

                MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
                mockHandler.SetResponse(new byte[] { 0x7F, 0x45, 0x4C, 0x46 });
                HttpClient httpClient = new HttpClient(mockHandler);
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                DistInstallCommand command = new DistInstallCommand(configManager, httpClient, new MockOutputWriter(), distributionClient, new MockPluginUrlResolver(), pluginsDirectory);

                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await command.ExecuteAsync("dup", "https://example.com/path/to/plugin-bin")
                );
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

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                DistInstallCommand command = new DistInstallCommand(configManager, httpClient, new MockOutputWriter(), distributionClient, new MockPluginUrlResolver(), pluginsDirectory);

                await Assert.ThrowsAsync<HttpRequestException>(
                    async () => await command.ExecuteAsync(null, "https://example.com/invalid-plugin")
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

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                DistInstallCommand command = new DistInstallCommand(configManager, httpClient, new MockOutputWriter(), distributionClient, new MockPluginUrlResolver(), pluginsDirectory);

                await command.ExecuteAsync(null, "https://example.com/path/to/byteshelf-dist");

                IReadOnlyDictionary<string, InstalledPluginInfo> plugins = await configManager.GetAllPluginsAsync();
                Assert.NotEmpty(plugins);
                Assert.Contains("byteshelf-dist", plugins.Values.First().Path);
            }
        }

        [Fact]
        public async Task ExecuteAsync_Throws_WhenPluginHasEmptyDefaultAliasAndNoAliasProvided()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                MockConfigManager configManager = new MockConfigManager();
                MockHttpMessageHandler mockHandler = new MockHttpMessageHandler();
                mockHandler.SetResponse(new byte[] { 0x7F, 0x45, 0x4C, 0x46 });
                HttpClient httpClient = new HttpClient(mockHandler);
                string pluginsDirectory = tempHelper.CreateTempDirectory("plugins");

                MockDistributionServiceClient distributionClient = new MockDistributionServiceClient();
                // Set custom service info with empty DefaultAlias
                distributionClient.CustomServiceInformation = new Updaemon.Common.Models.DistributionServiceInformation
                {
                    FullName = "Test Plugin",
                    DefaultAlias = "", // Empty alias
                    Description = "Test",
                    Version = "1.0.0",
                    Secrets = new List<Updaemon.Common.Models.DistributionSecretInfo>()
                };

                DistInstallCommand command = new DistInstallCommand(configManager, httpClient, new MockOutputWriter(), distributionClient, new MockPluginUrlResolver(), pluginsDirectory);

                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await command.ExecuteAsync(null, "https://example.com/path/to/plugin-bin")
                );
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

