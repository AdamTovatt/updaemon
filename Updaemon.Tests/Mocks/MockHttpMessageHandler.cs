using System.Net;

namespace Updaemon.Tests.Mocks
{
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private byte[]? _response;
        private Exception? _exception;
        private int _callCount = 0;

        public int CallCount => _callCount;

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
            _callCount++;

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
