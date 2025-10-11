using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Updaemon.Contracts;
using Updaemon.Contracts.Rpc;
using Updaemon.Contracts.Serialization;
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
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private bool _disposed;

        public DistributionServiceClient()
        {
        }

        public async Task ConnectAsync(string pluginExecutablePath)
        {
            if (!File.Exists(pluginExecutablePath))
            {
                throw new FileNotFoundException($"Plugin executable not found: {pluginExecutablePath}");
            }

            // Generate a unique pipe name
            string pipeName = $"updaemon_dist_{Guid.NewGuid():N}";

            // Start the plugin process with the pipe name as argument
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = pluginExecutablePath,
                Arguments = $"--pipe-name {pipeName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            _pluginProcess = Process.Start(startInfo);
            if (_pluginProcess == null)
            {
                throw new InvalidOperationException("Failed to start plugin process");
            }

            // Connect to the named pipe
            _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            
            // Wait for connection with timeout (10 seconds in milliseconds)
            await _pipeClient.ConnectAsync(10000);

            _reader = new StreamReader(_pipeClient, Encoding.UTF8);
            _writer = new StreamWriter(_pipeClient, Encoding.UTF8) { AutoFlush = true };
        }

        public async Task InitializeAsync(string? secrets)
        {
            await InvokeMethodAsync("InitializeAsync", secrets);
        }

        public async Task<Version?> GetLatestVersionAsync(string serviceName)
        {
            string? versionString = await InvokeMethodAsync<string?>("GetLatestVersionAsync", serviceName);
            
            if (string.IsNullOrEmpty(versionString))
            {
                return null;
            }

            return Version.Parse(versionString);
        }

        public async Task DownloadVersionAsync(string serviceName, Version version, string targetPath)
        {
            object parameters = new { serviceName, version = version.ToString(), targetPath };
            await InvokeMethodAsync("DownloadVersionAsync", parameters);
        }

        private async Task InvokeMethodAsync(string methodName, object? parameters)
        {
            await InvokeMethodAsync<object>(methodName, parameters);
        }

        private async Task<TResult?> InvokeMethodAsync<TResult>(string methodName, object? parameters)
        {
            if (_writer == null || _reader == null)
            {
                throw new InvalidOperationException("Client is not connected");
            }

            string requestId = Guid.NewGuid().ToString("N");
            
            RpcRequest request = new RpcRequest
            {
                Id = requestId,
                Method = methodName,
                Parameters = parameters != null ? JsonSerializer.Serialize(parameters, ContractsJsonContext.Default.Object) : null,
            };

            // Send request
            string requestJson = JsonSerializer.Serialize(request, ContractsJsonContext.Default.RpcRequest);
            await _writer.WriteLineAsync(requestJson);

            // Read response
            string? responseJson = await _reader.ReadLineAsync();
            if (responseJson == null)
            {
                throw new InvalidOperationException("No response received from plugin");
            }

            RpcResponse? response = JsonSerializer.Deserialize(responseJson, ContractsJsonContext.Default.RpcResponse);
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

            if (typeof(TResult) == typeof(string))
            {
                object? result = JsonSerializer.Deserialize(response.Result, ContractsJsonContext.Default.String);
                return (TResult?)result;
            }

            object? objResult = JsonSerializer.Deserialize(response.Result, ContractsJsonContext.Default.Object);
            return (TResult?)objResult;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_writer != null)
            {
                await _writer.DisposeAsync();
            }

            if (_reader != null)
            {
                _reader.Dispose();
            }

            if (_pipeClient != null)
            {
                await _pipeClient.DisposeAsync();
            }

            if (_pluginProcess != null && !_pluginProcess.HasExited)
            {
                _pluginProcess.Kill();
                await _pluginProcess.WaitForExitAsync();
                _pluginProcess.Dispose();
            }
        }
    }
}

