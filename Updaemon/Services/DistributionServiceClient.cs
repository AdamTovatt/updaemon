using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Updaemon.Common;
using Updaemon.Common.Models;
using Updaemon.Common.Rpc;
using Updaemon.Common.Serialization;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Client for communicating with distribution service plugins via named pipes.
    /// </summary>
    public class DistributionServiceClient : IDistributionServiceClient
    {
        private Process? _pluginProcess;
        private NamedPipeClientStream? _pipeClient;
        private bool _disposed;

        public DistributionServiceClient()
        {
        }

        public async Task ConnectAsync(string pluginExecutablePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(pluginExecutablePath))
            {
                throw new FileNotFoundException($"Plugin executable not found: {pluginExecutablePath}");
            }

            await CleanUpExistingConnectionAsync();

            _disposed = false;

            string pipeName = $"updaemon_dist_{Guid.NewGuid():N}";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = pluginExecutablePath,
                Arguments = $"--pipe-name {pipeName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            Process? startedProcess = Process.Start(startInfo);
            if (startedProcess == null)
            {
                throw new InvalidOperationException($"Failed to start plugin process: {pluginExecutablePath}");
            }

            _pluginProcess = startedProcess;

            StringBuilder stderrBuffer = new StringBuilder();
            _pluginProcess.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lock (stderrBuffer)
                    {
                        stderrBuffer.AppendLine(e.Data);
                    }
                }
            };
            _pluginProcess.BeginErrorReadLine();

            NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _pipeClient = pipeClient;

            try
            {
                Task connectTask = pipeClient.ConnectAsync(cancellationToken);
                Task exitTask = _pluginProcess.WaitForExitAsync(cancellationToken);

                Task completed = await Task.WhenAny(connectTask, exitTask);

                if (completed == exitTask && !pipeClient.IsConnected)
                {
                    string capturedStderr;
                    lock (stderrBuffer)
                    {
                        capturedStderr = stderrBuffer.ToString().Trim();
                    }

                    string detail = string.IsNullOrEmpty(capturedStderr)
                        ? "(no stderr output captured)"
                        : capturedStderr;

                    throw new InvalidOperationException(
                        $"Plugin process '{pluginExecutablePath}' exited (exit code {_pluginProcess.ExitCode}) before establishing pipe connection. Plugin stderr: {detail}");
                }

                await connectTask;
            }
            catch
            {
                await CleanUpExistingConnectionAsync();
                throw;
            }
        }

        private async Task CleanUpExistingConnectionAsync()
        {
            if (_pipeClient != null)
            {
                await _pipeClient.DisposeAsync();
                _pipeClient = null;
            }

            if (_pluginProcess != null)
            {
                try
                {
                    if (!_pluginProcess.HasExited)
                    {
                        _pluginProcess.Kill();
                        await _pluginProcess.WaitForExitAsync();
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process was never associated or already disposed - nothing to clean up.
                }

                _pluginProcess.Dispose();
                _pluginProcess = null;
            }
        }

        public async Task InitializeAsync(string? secrets, CancellationToken cancellationToken = default)
        {
            SecretCollection secretCollection = SecretCollection.FromString(secrets);
            Dictionary<string, string> secretsDictionary = secretCollection.ToDictionary();
            await InvokeMethodAsync("InitializeAsync", secretsDictionary, cancellationToken);
        }

        public async Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            string? versionString = await InvokeMethodAsync<string?>("GetLatestVersionAsync", serviceName, cancellationToken);

            if (string.IsNullOrEmpty(versionString))
            {
                return null;
            }

            return Version.Parse(versionString);
        }

        public async Task DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>
            {
                { "serviceName", serviceName },
                { "version", version.ToString() },
                { "targetPath", targetPath },
            };
            await InvokeMethodAsync("DownloadVersionAsync", parameters, cancellationToken);
        }

        public async Task<DistributionServiceInformation> GetServiceInformationAsync(CancellationToken cancellationToken = default)
        {
            DistributionServiceInformation? info = await InvokeMethodAsync<DistributionServiceInformation>("GetServiceInformation", null, cancellationToken);
            if (info == null)
            {
                throw new InvalidOperationException("Plugin returned null service information");
            }

            return info;
        }

        private async Task InvokeMethodAsync(string methodName, object? parameters, CancellationToken cancellationToken)
        {
            await InvokeMethodAsync<object>(methodName, parameters, cancellationToken);
        }

        private async Task<TResult?> InvokeMethodAsync<TResult>(string methodName, object? parameters, CancellationToken cancellationToken)
        {
            if (_pipeClient == null || !_pipeClient.IsConnected)
            {
                throw new InvalidOperationException("Client is not connected");
            }

            string requestId = Guid.NewGuid().ToString("N");

            RpcRequest request = new RpcRequest
            {
                Id = requestId,
                Method = methodName,
                Parameters = parameters != null ? JsonSerializer.Serialize(parameters, CommonJsonContext.Default.Object) : null,
            };

            // Send request
            string requestJson = JsonSerializer.Serialize(request, CommonJsonContext.Default.RpcRequest);
            byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson + "\n");
            await _pipeClient.WriteAsync(requestBytes, 0, requestBytes.Length, cancellationToken);
            await _pipeClient.FlushAsync(cancellationToken);

            // Read response
            string? responseJson = await ReadLineAsync(_pipeClient, cancellationToken);
            if (responseJson == null)
            {
                throw new InvalidOperationException("No response received from plugin");
            }

            RpcResponse? response = JsonSerializer.Deserialize(responseJson, CommonJsonContext.Default.RpcResponse);
            if (response == null)
            {
                throw new InvalidOperationException("Failed to deserialize response");
            }

            if (!response.Success)
            {
                throw new InvalidOperationException($"RPC call failed: {response.Error}");
            }

            if (typeof(TResult) == typeof(object) || response.Result == null)
            {
                return default;
            }

            // Handle string return types
            if (typeof(TResult) == typeof(string))
            {
                object? result = JsonSerializer.Deserialize(response.Result, CommonJsonContext.Default.String);
                return (TResult?)result;
            }

            // Handle nullable string return type
            Type resultType = typeof(TResult);
            if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Nullable<>) && resultType.GetGenericArguments()[0] == typeof(string))
            {
                object? result = JsonSerializer.Deserialize(response.Result, CommonJsonContext.Default.String);
                return (TResult?)result;
            }

            // Handle DistributionServiceInformation (and other types from CommonJsonContext)
            if (typeof(TResult) == typeof(DistributionServiceInformation))
            {
                DistributionServiceInformation? result = JsonSerializer.Deserialize(response.Result, CommonJsonContext.Default.DistributionServiceInformation);
                return (TResult?)(object?)result;
            }

            // Fallback: try to deserialize as object (may not work for all types)
            object? objResult = JsonSerializer.Deserialize(response.Result, CommonJsonContext.Default.Object);
            return (TResult?)objResult;
        }

        private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
        {
            StringBuilder lineBuilder = new StringBuilder();
            byte[] buffer = new byte[1];

            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, 1, cancellationToken);
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

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            await CleanUpExistingConnectionAsync();
        }
    }
}

