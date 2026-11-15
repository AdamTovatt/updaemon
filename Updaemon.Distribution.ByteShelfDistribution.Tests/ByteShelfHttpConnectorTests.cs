using System.Net;
using Updaemon.Common;
using Updaemon.Common.Models;
using Updaemon.Common.Utilities;

namespace Updaemon.Distribution.ByteShelfDistribution.Tests
{
    public class ByteShelfHttpConnectorTests
    {
        /// <summary>Answers every request 401, as a server does for a key it does not know, and keeps them.</summary>
        private sealed class RefusingHandler : HttpMessageHandler
        {
            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }
        }

        [Fact]
        public async Task GetLatestVersionAsync_OverHttp_ListsTheAppWithTheKeyAndReadsA401AsARejectedKey()
        {
            RefusingHandler handler = new RefusingHandler();
            using ByteShelfHttpConnector connector = new ByteShelfHttpConnector(handler);
            ByteShelfDistributionService service = new ByteShelfDistributionService(new VersionParser(), Substitute.For<IDownloadPostProcessor>(), connector.Connect);
            await service.InitializeAsync(SecretCollection.FromString("byteShelfUrl=https://shelf.example/base\nbyteShelfKey=bsk_test"));

            CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(() => service.GetLatestVersionAsync("tools/myapp"));

            HttpRequestMessage request = Assert.Single(handler.Requests);
            Assert.Equal("https://shelf.example/base/api/shelves/tools/entries?path=%2Fmyapp", request.RequestUri!.AbsoluteUri);
            Assert.Equal(new[] { "bsk_test" }, request.Headers.GetValues("X-Api-Key"));
            Assert.Equal(CredentialProblem.Rejected, ex.Need.Problem);
        }
    }
}
