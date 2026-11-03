using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Updaemon.Common;
using Updaemon.Common.Models;
using Updaemon.Common.Rpc;
using Updaemon.Common.Serialization;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Client for communicating with distribution service plugins via named pipes.
    /// </summary>
    /// <remarks>
    /// Reuse contract: a single instance can be driven through multiple
    /// Connect/...DisposeAsync cycles. Each ConnectAsync resets internal state
    /// so the same instance can host a new plugin process. After DisposeAsync,
    /// calling ConnectAsync again is supported and revives the client.
    /// </remarks>
    public class DistributionServiceClient : IDistributionServiceClient
    {
        private readonly string _runtimeIdentifier;
        private Process? _pluginProcess;
        private NamedPipeClientStream? _pipeClient;
        private StringBuilder? _stderrBuffer;
        private bool _disposed;

        /// <param name="platform">This machine; its runtime identifier is passed to the plugin in each download.</param>
        public DistributionServiceClient(HostPlatform platform)
        {
            _runtimeIdentifier = platform.RuntimeIdentifier;
        }

        public async Task<DistributionServiceInformation> ConnectAsync(string pluginExecutablePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(pluginExecutablePath))
            {
                throw new FileNotFoundException($"Plugin executable not found: {pluginExecutablePath}");
            }

            await CleanupExistingConnectionAsync();

            _disposed = false;

            // Keep the pipe name short. .NET hosts named pipes on Unix as a domain socket
            // at <TMPDIR>/CoreFxPipe_<name>, and macOS limits sun_path to 104 chars total.
            // <TMPDIR> on macOS (e.g. /var/folders/…/T/) is already ~50 chars, so the
            // remaining budget after "CoreFxPipe_" is ~40 chars. 16 hex chars (64 bits)
            // is more than enough collision resistance for a single host's concurrent pipes.
            string pipeName = "upd_" + Guid.NewGuid().ToString("N").Substring(0, 12);

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
            _stderrBuffer = stderrBuffer;
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
                    throw new InvalidOperationException(
                        $"Plugin process '{pluginExecutablePath}' exited (exit code {_pluginProcess.ExitCode}) before establishing pipe connection. Plugin stderr: {GetCapturedStderrOrPlaceholder()}");
                }

                await connectTask;

                string? informationJson = await InvokeMethodAsync("GetServiceInformation", null, cancellationToken);
                return ReadServiceInformation(informationJson, pluginExecutablePath);
            }
            catch
            {
                await CleanupExistingConnectionAsync();
                throw;
            }
        }

        /// <summary>
        /// Reads a plugin's service information, refusing a plugin that speaks another protocol
        /// version, so no call is ever made to a plugin that would misread it.
        /// </summary>
        internal static DistributionServiceInformation ReadServiceInformation(string? informationJson, string pluginExecutablePath)
        {
            if (informationJson == null)
            {
                throw new PluginException($"The plugin at {pluginExecutablePath} returned no service information.");
            }

            int protocolVersion = PluginProtocol.UnversionedPluginVersion;
            string propertyName = JsonNamingPolicy.CamelCase.ConvertName(nameof(DistributionServiceInformation.ProtocolVersion));
            using (JsonDocument document = JsonDocument.Parse(informationJson))
            {
                if (document.RootElement.TryGetProperty(propertyName, out JsonElement element)
                    && element.ValueKind == JsonValueKind.Number
                    && element.TryGetInt32(out int reportedVersion))
                {
                    protocolVersion = reportedVersion;
                }
            }

            if (protocolVersion != PluginProtocol.Version)
            {
                throw new PluginException(
                    $"The plugin at {pluginExecutablePath} speaks plugin protocol {protocolVersion}, but this updaemon speaks protocol {PluginProtocol.Version}. " +
                    "Reinstall the plugin with 'updaemon dist-install [--as <alias>] <plugin-name|url>'.");
            }

            DistributionServiceInformation? information = JsonSerializer.Deserialize(informationJson, CommonJsonContext.Default.DistributionServiceInformation);
            if (information == null)
            {
                throw new PluginException($"The plugin at {pluginExecutablePath} returned no service information.");
            }

            return information;
        }

        private string GetCapturedStderrOrPlaceholder()
        {
            StringBuilder? buffer = _stderrBuffer;
            if (buffer == null)
            {
                return "(no stderr output captured)";
            }

            string captured;
            lock (buffer)
            {
                captured = buffer.ToString().Trim();
            }

            return string.IsNullOrEmpty(captured) ? "(no stderr output captured)" : captured;
        }

        private async Task CleanupExistingConnectionAsync()
        {
            // Order matters: kill the process first so any pipe disposal failure
            // can't leave a child process orphaned. Then dispose the pipe in its
            // own try so an exception there still lets us null out the fields.
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
                    // Process was never associated or already disposed.
                }

                try
                {
                    _pluginProcess.Dispose();
                }
                catch
                {
                    // Best effort; the field reset below is what protects subsequent calls.
                }

                _pluginProcess = null;
            }

            if (_pipeClient != null)
            {
                try
                {
                    await _pipeClient.DisposeAsync();
                }
                catch
                {
                    // Best effort; field reset below is what matters.
                }

                _pipeClient = null;
            }

            _stderrBuffer = null;
        }

        public async Task InitializeAsync(string? secrets, CancellationToken cancellationToken = default)
        {
            SecretCollection secretCollection = SecretCollection.FromString(secrets);
            Dictionary<string, string> secretsDictionary = secretCollection.ToDictionary();
            await InvokeMethodAsync(
                "InitializeAsync",
                JsonSerializer.Serialize(secretsDictionary, CommonJsonContext.Default.DictionaryStringString),
                cancellationToken);
        }

        public async Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            string? resultJson = await InvokeMethodAsync(
                "GetLatestVersionAsync",
                JsonSerializer.Serialize(serviceName, CommonJsonContext.Default.String),
                cancellationToken);

            string? versionString = resultJson == null ? null : JsonSerializer.Deserialize(resultJson, CommonJsonContext.Default.String);
            if (string.IsNullOrEmpty(versionString))
            {
                return null;
            }

            return Version.Parse(versionString);
        }

        public async Task<DownloadResult> DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default)
        {
            DownloadVersionParameters parameters = new DownloadVersionParameters
            {
                ServiceName = serviceName,
                Version = version.ToString(),
                RuntimeIdentifier = _runtimeIdentifier,
                TargetPath = targetPath,
            };
            string? resultJson = await InvokeMethodAsync(
                "DownloadVersionAsync",
                JsonSerializer.Serialize(parameters, CommonJsonContext.Default.DownloadVersionParameters),
                cancellationToken);

            return (resultJson == null ? null : JsonSerializer.Deserialize(resultJson, CommonJsonContext.Default.DownloadResult))
                ?? throw new PluginException("the plugin's download reported no result, which its protocol version requires.");
        }

        /// <summary>
        /// Sends one request and returns the result's JSON, or null for a call without a result.
        /// A failed call throws <see cref="PluginException"/> carrying the plugin's message.
        /// </summary>
        private async Task<string?> InvokeMethodAsync(string methodName, string? parametersJson, CancellationToken cancellationToken)
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
                Parameters = parametersJson,
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
                throw new InvalidOperationException(
                    $"No response received from plugin. Plugin stderr: {GetCapturedStderrOrPlaceholder()}");
            }

            RpcResponse? response = JsonSerializer.Deserialize(responseJson, CommonJsonContext.Default.RpcResponse);
            if (response == null)
            {
                throw new InvalidOperationException("Failed to deserialize response");
            }

            if (!response.Success)
            {
                throw new PluginException(
                    string.IsNullOrWhiteSpace(response.Error)
                        ? $"The plugin failed {methodName} without saying why."
                        : response.Error);
            }

            return response.Result;
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

            await CleanupExistingConnectionAsync();
        }
    }
}

