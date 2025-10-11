using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Updaemon.Common;
using Updaemon.Common.Hosting;
using Updaemon.Common.Rpc;
using Updaemon.Common.Serialization;

namespace Updaemon.Common.Tests.Hosting
{
    public class DistributionServiceHostTests
    {
        [Fact]
        public async Task RunAsync_MissingPipeNameArgument_ThrowsArgumentException()
        {
            string[] args = new string[] { };
            TestDistributionService service = new TestDistributionService();

            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
                async () => await DistributionServiceHost.RunAsync(args, service)
            );

            Assert.Contains("--pipe-name", exception.Message);
        }

        [Fact]
        public async Task RunAsync_EmptyPipeNameArgument_ThrowsArgumentException()
        {
            string[] args = new string[] { "--pipe-name", "" };
            TestDistributionService service = new TestDistributionService();

            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
                async () => await DistributionServiceHost.RunAsync(args, service)
            );

            Assert.Contains("Pipe name cannot be empty", exception.Message);
        }

        [Fact]
        public async Task RunAsync_WhitespacePipeNameArgument_ThrowsArgumentException()
        {
            string[] args = new string[] { "--pipe-name", "   " };
            TestDistributionService service = new TestDistributionService();

            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
                async () => await DistributionServiceHost.RunAsync(args, service)
            );

            Assert.Contains("Pipe name cannot be empty", exception.Message);
        }

        [Fact]
        public async Task RunAsync_InitializeAsyncMethod_CallsImplementation()
        {
            string pipeName = $"test_pipe_{Guid.NewGuid():N}";
            string[] args = new string[] { "--pipe-name", pipeName };
            TestDistributionService service = new TestDistributionService();

            Task serverTask = Task.Run(async () =>
            {
                await DistributionServiceHost.RunAsync(args, service);
            });

            await Task.Delay(100); // Give server time to start

            using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
            {
                await client.ConnectAsync(5000);

                RpcRequest request = new RpcRequest
                {
                    Id = "test-1",
                    Method = "InitializeAsync",
                    Parameters = JsonSerializer.Serialize("tenantId=123\napiKey=abc", CommonJsonContext.Default.String),
                };

                string requestJson = JsonSerializer.Serialize(request, CommonJsonContext.Default.RpcRequest);
                byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson + "\n");
                await client.WriteAsync(requestBytes, 0, requestBytes.Length);
                await client.FlushAsync();

                string? responseJson = await ReadLineAsync(client);
                Assert.NotNull(responseJson);

                RpcResponse? response = JsonSerializer.Deserialize(responseJson, CommonJsonContext.Default.RpcResponse);
                Assert.NotNull(response);
                Assert.Equal("test-1", response.Id);
                Assert.True(response.Success);
                Assert.Equal("tenantId=123\napiKey=abc", service.LastInitializedSecrets);
            }
        }

        [Fact]
        public async Task RunAsync_GetLatestVersionAsyncMethod_ReturnsVersion()
        {
            string pipeName = $"test_pipe_{Guid.NewGuid():N}";
            string[] args = new string[] { "--pipe-name", pipeName };
            TestDistributionService service = new TestDistributionService
            {
                LatestVersion = new Version(1, 2, 3),
            };

            Task serverTask = Task.Run(async () =>
            {
                await DistributionServiceHost.RunAsync(args, service);
            });

            await Task.Delay(100);

            using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
            {
                await client.ConnectAsync(5000);

                RpcRequest request = new RpcRequest
                {
                    Id = "test-2",
                    Method = "GetLatestVersionAsync",
                    Parameters = JsonSerializer.Serialize("TestService", CommonJsonContext.Default.String),
                };

                string requestJson = JsonSerializer.Serialize(request, CommonJsonContext.Default.RpcRequest);
                byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson + "\n");
                await client.WriteAsync(requestBytes, 0, requestBytes.Length);
                await client.FlushAsync();

                string? responseJson = await ReadLineAsync(client);
                Assert.NotNull(responseJson);

                RpcResponse? response = JsonSerializer.Deserialize(responseJson, CommonJsonContext.Default.RpcResponse);
                Assert.NotNull(response);
                Assert.Equal("test-2", response.Id);
                Assert.True(response.Success);

                string? versionString = JsonSerializer.Deserialize(response.Result!, CommonJsonContext.Default.String);
                Assert.Equal("1.2.3", versionString);
                Assert.Equal("TestService", service.LastServiceName);
            }
        }

        [Fact]
        public async Task RunAsync_GetLatestVersionAsyncMethod_ReturnsNullWhenNoVersion()
        {
            string pipeName = $"test_pipe_{Guid.NewGuid():N}";
            string[] args = new string[] { "--pipe-name", pipeName };
            TestDistributionService service = new TestDistributionService
            {
                LatestVersion = null,
            };

            Task serverTask = Task.Run(async () =>
            {
                await DistributionServiceHost.RunAsync(args, service);
            });

            await Task.Delay(100);

            using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
            {
                await client.ConnectAsync(5000);

                RpcRequest request = new RpcRequest
                {
                    Id = "test-3",
                    Method = "GetLatestVersionAsync",
                    Parameters = JsonSerializer.Serialize("NonExistent", CommonJsonContext.Default.String),
                };

                string requestJson = JsonSerializer.Serialize(request, CommonJsonContext.Default.RpcRequest);
                byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson + "\n");
                await client.WriteAsync(requestBytes, 0, requestBytes.Length);
                await client.FlushAsync();

                string? responseJson = await ReadLineAsync(client);
                Assert.NotNull(responseJson);

                RpcResponse? response = JsonSerializer.Deserialize(responseJson, CommonJsonContext.Default.RpcResponse);
                Assert.NotNull(response);
                Assert.True(response.Success);

                string? versionString = JsonSerializer.Deserialize(response.Result!, CommonJsonContext.Default.String);
                Assert.Null(versionString);
            }
        }

        [Fact]
        public async Task RunAsync_DownloadVersionAsyncMethod_CallsImplementation()
        {
            string pipeName = $"test_pipe_{Guid.NewGuid():N}";
            string[] args = new string[] { "--pipe-name", pipeName };
            TestDistributionService service = new TestDistributionService();

            Task serverTask = Task.Run(async () =>
            {
                await DistributionServiceHost.RunAsync(args, service);
            });

            await Task.Delay(100);

            using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
            {
                await client.ConnectAsync(5000);

                RpcRequest request = new RpcRequest
                {
                    Id = "test-4",
                    Method = "DownloadVersionAsync",
                    Parameters = "{\"serviceName\":\"TestService\",\"version\":\"2.0.1\",\"targetPath\":\"/opt/test/2.0.1\"}",
                };

                string requestJson = JsonSerializer.Serialize(request, CommonJsonContext.Default.RpcRequest);
                byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson + "\n");
                await client.WriteAsync(requestBytes, 0, requestBytes.Length);
                await client.FlushAsync();

                string? responseJson = await ReadLineAsync(client);
                Assert.NotNull(responseJson);

                RpcResponse? response = JsonSerializer.Deserialize(responseJson, CommonJsonContext.Default.RpcResponse);
                Assert.NotNull(response);
                Assert.Equal("test-4", response.Id);
                Assert.True(response.Success);
                Assert.Equal("TestService", service.LastDownloadServiceName);
                Assert.Equal(new Version(2, 0, 1), service.LastDownloadVersion);
                Assert.Equal("/opt/test/2.0.1", service.LastDownloadTargetPath);
            }
        }

        [Fact]
        public async Task RunAsync_UnknownMethod_ReturnsError()
        {
            string pipeName = $"test_pipe_{Guid.NewGuid():N}";
            string[] args = new string[] { "--pipe-name", pipeName };
            TestDistributionService service = new TestDistributionService();

            Task serverTask = Task.Run(async () =>
            {
                await DistributionServiceHost.RunAsync(args, service);
            });

            await Task.Delay(100);

            using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
            {
                await client.ConnectAsync(5000);

                RpcRequest request = new RpcRequest
                {
                    Id = "test-5",
                    Method = "UnknownMethod",
                    Parameters = null,
                };

                string requestJson = JsonSerializer.Serialize(request, CommonJsonContext.Default.RpcRequest);
                byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson + "\n");
                await client.WriteAsync(requestBytes, 0, requestBytes.Length);
                await client.FlushAsync();

                string? responseJson = await ReadLineAsync(client);
                Assert.NotNull(responseJson);

                RpcResponse? response = JsonSerializer.Deserialize(responseJson, CommonJsonContext.Default.RpcResponse);
                Assert.NotNull(response);
                Assert.Equal("test-5", response.Id);
                Assert.False(response.Success);
                Assert.Contains("Unknown method", response.Error);
            }
        }

        [Fact]
        public async Task RunAsync_ExceptionInImplementation_ReturnsErrorWithStackTrace()
        {
            string pipeName = $"test_pipe_{Guid.NewGuid():N}";
            string[] args = new string[] { "--pipe-name", pipeName };
            TestDistributionService service = new TestDistributionService
            {
                ShouldThrowException = true,
            };

            Task serverTask = Task.Run(async () =>
            {
                await DistributionServiceHost.RunAsync(args, service);
            });

            await Task.Delay(100);

            using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
            {
                await client.ConnectAsync(5000);

                RpcRequest request = new RpcRequest
                {
                    Id = "test-6",
                    Method = "InitializeAsync",
                    Parameters = null,
                };

                string requestJson = JsonSerializer.Serialize(request, CommonJsonContext.Default.RpcRequest);
                byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson + "\n");
                await client.WriteAsync(requestBytes, 0, requestBytes.Length);
                await client.FlushAsync();

                string? responseJson = await ReadLineAsync(client);
                Assert.NotNull(responseJson);

                RpcResponse? response = JsonSerializer.Deserialize(responseJson, CommonJsonContext.Default.RpcResponse);
                Assert.NotNull(response);
                Assert.Equal("test-6", response.Id);
                Assert.False(response.Success);
                Assert.Contains("Test exception", response.Error);
                Assert.Contains("at ", response.Error); // Should contain stack trace
            }
        }

        private static async Task<string?> ReadLineAsync(Stream stream)
        {
            StringBuilder lineBuilder = new StringBuilder();
            byte[] buffer = new byte[1];

            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, 1);
                if (bytesRead == 0)
                {
                    return lineBuilder.Length > 0 ? lineBuilder.ToString() : null;
                }

                char c = (char)buffer[0];
                if (c == '\n')
                {
                    return lineBuilder.ToString();
                }
                else if (c != '\r')
                {
                    lineBuilder.Append(c);
                }
            }
        }

        private class TestDistributionService : IDistributionService
        {
            public string? LastInitializedSecrets { get; private set; }
            public string? LastServiceName { get; private set; }
            public string? LastDownloadServiceName { get; private set; }
            public Version? LastDownloadVersion { get; private set; }
            public string? LastDownloadTargetPath { get; private set; }
            public Version? LatestVersion { get; set; }
            public bool ShouldThrowException { get; set; }

            public Task InitializeAsync(string? secrets)
            {
                if (ShouldThrowException)
                {
                    throw new InvalidOperationException("Test exception");
                }

                LastInitializedSecrets = secrets;
                return Task.CompletedTask;
            }

            public Task<Version?> GetLatestVersionAsync(string serviceName)
            {
                if (ShouldThrowException)
                {
                    throw new InvalidOperationException("Test exception");
                }

                LastServiceName = serviceName;
                return Task.FromResult(LatestVersion);
            }

            public Task DownloadVersionAsync(string serviceName, Version version, string targetPath)
            {
                if (ShouldThrowException)
                {
                    throw new InvalidOperationException("Test exception");
                }

                LastDownloadServiceName = serviceName;
                LastDownloadVersion = version;
                LastDownloadTargetPath = targetPath;
                return Task.CompletedTask;
            }
        }
    }
}

