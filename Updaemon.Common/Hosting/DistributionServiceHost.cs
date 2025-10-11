using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Updaemon.Common.Rpc;
using Updaemon.Common.Serialization;

namespace Updaemon.Common.Hosting
{
    /// <summary>
    /// Provides hosting infrastructure for distribution service plugins.
    /// Handles named pipe server setup, RPC communication, and method routing.
    /// </summary>
    public static class DistributionServiceHost
    {
        /// <summary>
        /// Runs a distribution service plugin, handling all named pipe server infrastructure.
        /// </summary>
        /// <param name="args">Command-line arguments (must include --pipe-name)</param>
        /// <param name="implementation">The IDistributionService implementation to host</param>
        /// <exception cref="ArgumentException">Thrown when --pipe-name argument is missing or invalid</exception>
        public static async Task RunAsync(string[] args, IDistributionService implementation)
        {
            string pipeName = ParsePipeName(args);

            using (NamedPipeServerStream pipeServer = new NamedPipeServerStream(
                pipeName: pipeName,
                direction: PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                transmissionMode: PipeTransmissionMode.Byte,
                options: PipeOptions.Asynchronous))
            {
                await pipeServer.WaitForConnectionAsync();
                await HandleRequestsAsync(pipeServer, implementation);
            }
        }

        /// <summary>
        /// Parses the --pipe-name argument from command-line arguments.
        /// </summary>
        private static string ParsePipeName(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--pipe-name")
                {
                    string pipeName = args[i + 1];
                    if (string.IsNullOrWhiteSpace(pipeName))
                    {
                        throw new ArgumentException("Pipe name cannot be empty");
                    }
                    return pipeName;
                }
            }

            throw new ArgumentException("Missing required argument: --pipe-name <name>");
        }

        /// <summary>
        /// Handles the RPC request/response loop.
        /// </summary>
        private static async Task HandleRequestsAsync(
            Stream stream,
            IDistributionService implementation)
        {
            byte[] buffer = new byte[4096];
            StringBuilder messageBuilder = new StringBuilder();

            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    break;
                }

                string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                messageBuilder.Append(chunk);

                string accumulated = messageBuilder.ToString();
                int newlineIndex = accumulated.IndexOf('\n');

                while (newlineIndex >= 0)
                {
                    string requestJson = accumulated.Substring(0, newlineIndex);
                    accumulated = accumulated.Substring(newlineIndex + 1);
                    messageBuilder.Clear();
                    messageBuilder.Append(accumulated);

                    if (!string.IsNullOrWhiteSpace(requestJson))
                    {
                        RpcRequest? request = JsonSerializer.Deserialize(requestJson, CommonJsonContext.Default.RpcRequest);
                        if (request != null)
                        {
                            RpcResponse response = await HandleRequestAsync(request, implementation);
                            string responseJson = JsonSerializer.Serialize(response, CommonJsonContext.Default.RpcResponse);
                            byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson + "\n");
                            await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                            await stream.FlushAsync();
                        }
                    }

                    newlineIndex = accumulated.IndexOf('\n');
                }
            }
        }

        /// <summary>
        /// Handles a single RPC request by routing it to the appropriate method.
        /// </summary>
        private static async Task<RpcResponse> HandleRequestAsync(
            RpcRequest request,
            IDistributionService implementation)
        {
            try
            {
                switch (request.Method)
                {
                    case "InitializeAsync":
                        return await HandleInitializeAsync(request, implementation);

                    case "GetLatestVersionAsync":
                        return await HandleGetLatestVersionAsync(request, implementation);

                    case "DownloadVersionAsync":
                        return await HandleDownloadVersionAsync(request, implementation);

                    default:
                        return new RpcResponse
                        {
                            Id = request.Id,
                            Success = false,
                            Error = $"Unknown method: {request.Method}",
                        };
                }
            }
            catch (Exception ex)
            {
                return new RpcResponse
                {
                    Id = request.Id,
                    Success = false,
                    Error = $"{ex.Message}\n{ex.StackTrace}",
                };
            }
        }

        /// <summary>
        /// Handles InitializeAsync method invocation.
        /// </summary>
        private static async Task<RpcResponse> HandleInitializeAsync(
            RpcRequest request,
            IDistributionService implementation)
        {
            string? secrets = null;
            if (request.Parameters != null)
            {
                secrets = JsonSerializer.Deserialize(request.Parameters, CommonJsonContext.Default.String);
            }

            await implementation.InitializeAsync(secrets);

            return new RpcResponse
            {
                Id = request.Id,
                Success = true,
                Result = null,
            };
        }

        /// <summary>
        /// Handles GetLatestVersionAsync method invocation.
        /// </summary>
        private static async Task<RpcResponse> HandleGetLatestVersionAsync(
            RpcRequest request,
            IDistributionService implementation)
        {
            if (request.Parameters == null)
            {
                throw new ArgumentException("GetLatestVersionAsync requires serviceName parameter");
            }

            string? serviceName = JsonSerializer.Deserialize(request.Parameters, CommonJsonContext.Default.String);
            if (serviceName == null)
            {
                throw new ArgumentException("serviceName cannot be null");
            }

            Version? version = await implementation.GetLatestVersionAsync(serviceName);

            string? result = version?.ToString();
            string? resultJson = JsonSerializer.Serialize(result, CommonJsonContext.Default.String);

            return new RpcResponse
            {
                Id = request.Id,
                Success = true,
                Result = resultJson,
            };
        }

        /// <summary>
        /// Handles DownloadVersionAsync method invocation.
        /// </summary>
        private static async Task<RpcResponse> HandleDownloadVersionAsync(
            RpcRequest request,
            IDistributionService implementation)
        {
            if (request.Parameters == null)
            {
                throw new ArgumentException("DownloadVersionAsync requires parameters");
            }

            JsonDocument doc = JsonDocument.Parse(request.Parameters);
            JsonElement root = doc.RootElement;

            string? serviceName = root.GetProperty("serviceName").GetString();
            string? versionString = root.GetProperty("version").GetString();
            string? targetPath = root.GetProperty("targetPath").GetString();

            if (serviceName == null || versionString == null || targetPath == null)
            {
                throw new ArgumentException("serviceName, version, and targetPath are required");
            }

            Version version = Version.Parse(versionString);

            await implementation.DownloadVersionAsync(serviceName, version, targetPath);

            return new RpcResponse
            {
                Id = request.Id,
                Success = true,
                Result = null,
            };
        }
    }
}

