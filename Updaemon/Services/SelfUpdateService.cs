using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Updaemon.Common.Utilities;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Serialization;

namespace Updaemon.Services
{
    /// <summary>
    /// Service for checking and updating updaemon itself.
    /// </summary>
    public class SelfUpdateService : ISelfUpdateService
    {
        private readonly HttpClient _httpClient;
        private readonly IOutputWriter _outputWriter;
        private readonly VersionParser _versionParser;
        private readonly string _currentVersion;

        public SelfUpdateService(HttpClient httpClient, IOutputWriter outputWriter)
        {
            _httpClient = httpClient;
            _outputWriter = outputWriter;
            _versionParser = new VersionParser();

            // Get current version from assembly (AOT-friendly)
            Assembly assembly = Assembly.GetExecutingAssembly();
            _currentVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                           ?? assembly.GetName().Version?.ToString()
                           ?? "unknown";
        }

        public async Task<bool> CheckAndUpdateAsync(CancellationToken cancellationToken = default)
        {
            _outputWriter.WriteLine("\n=== Checking for updaemon updates ===");

            try
            {
                // Get latest version from GitHub
                Version? latestVersion = await GetLatestVersionAsync(cancellationToken);
                if (latestVersion == null)
                {
                    _outputWriter.WriteLine("Could not determine latest version");
                    return false;
                }

                // Parse current version
                Version? currentVersion = null;
                if (_currentVersion != "unknown" && Version.TryParse(_currentVersion, out Version? parsed))
                {
                    currentVersion = parsed;
                }

                if (currentVersion != null)
                {
                    _outputWriter.WriteLine($"Current version: {currentVersion}");
                }
                else
                {
                    _outputWriter.WriteLine($"Current version: {_currentVersion} (could not parse)");
                }

                _outputWriter.WriteLine($"Latest version: {latestVersion}");

                // Check if update is needed
                if (currentVersion != null && latestVersion <= currentVersion)
                {
                    _outputWriter.WriteLine("Updaemon is already up to date");
                    return false;
                }

                // Download and update
                _outputWriter.WriteLine("A newer version is available. Downloading...");
                bool updateTriggered = await DownloadAndUpdateAsync(latestVersion, cancellationToken);
                return updateTriggered;
            }
            catch (Exception ex)
            {
                _outputWriter.WriteError($"Error checking for updates: {ex.Message}");
                return false;
            }
        }

        private async Task<Version?> GetLatestVersionAsync(CancellationToken cancellationToken)
        {
            string url = "https://api.github.com/repos/AdamTovatt/updaemon/releases/latest";

            HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            GitHubRelease? release = JsonSerializer.Deserialize(jsonContent, UpdaemonJsonContext.Default.GitHubRelease);

            if (release == null || string.IsNullOrEmpty(release.TagName))
            {
                return null;
            }

            // Parse version from tag name (e.g., "v0.6.0" -> Version)
            Version? version = _versionParser.Parse(release.TagName);
            return version;
        }

        private async Task<bool> DownloadAndUpdateAsync(Version version, CancellationToken cancellationToken)
        {
            try
            {
                // Get download URL
                string url = "https://api.github.com/repos/AdamTovatt/updaemon/releases/latest";
                HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                string jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                GitHubRelease? release = JsonSerializer.Deserialize(jsonContent, UpdaemonJsonContext.Default.GitHubRelease);

                if (release == null || release.Assets.Length == 0)
                {
                    _outputWriter.WriteError("No download URL found in release");
                    return false;
                }

                string downloadUrl = release.Assets[0].BrowserDownloadUrl;
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    _outputWriter.WriteError("Download URL is empty");
                    return false;
                }

                // Download binary
                _outputWriter.WriteLine($"Downloading from: {downloadUrl}");
                byte[] binaryData = await _httpClient.GetByteArrayAsync(downloadUrl, cancellationToken);
                _outputWriter.WriteLine($"Downloaded {binaryData.Length} bytes");

                // Get target path
                string? targetPath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(targetPath))
                {
                    targetPath = "/usr/local/bin/updaemon";
                }

                // Create temp file
                string tempFile = $"/tmp/updaemon-{version}";
                await File.WriteAllBytesAsync(tempFile, binaryData, cancellationToken);

                // Make temp file executable
                try
                {
                    Process.Start("chmod", $"+x {tempFile}")?.WaitForExit();
                }
                catch
                {
                    _outputWriter.WriteLine("Warning: Could not make downloaded file executable. Helper script will handle this.");
                }

                // Create helper script
                int processId = Process.GetCurrentProcess().Id;
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string scriptPath = $"/tmp/updaemon-update-{processId}-{timestamp}.sh";

                string scriptContent = GenerateHelperScript(tempFile, targetPath, processId);
                await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken);

                // Make script executable
                try
                {
                    Process.Start("chmod", $"+x {scriptPath}")?.WaitForExit();
                }
                catch
                {
                    _outputWriter.WriteError("Error: Could not make helper script executable");
                    return false;
                }

                // Launch helper script in background
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = scriptPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                Process? scriptProcess = Process.Start(startInfo);
                if (scriptProcess == null)
                {
                    _outputWriter.WriteError("Error: Failed to start helper script");
                    return false;
                }

                _outputWriter.WriteLine("Update downloaded. Helper script will replace updaemon after this process exits.");
                _outputWriter.WriteLine("Please wait for the update to complete before running updaemon again.");

                return true;
            }
            catch (Exception ex)
            {
                _outputWriter.WriteError($"Error downloading update: {ex.Message}");
                return false;
            }
        }

        private static string GenerateHelperScript(string tempFile, string targetPath, int parentPid)
        {
            // Escape paths for bash script
            string escapedTempFile = tempFile.Replace("\"", "\\\"").Replace("$", "\\$");
            string escapedTargetPath = targetPath.Replace("\"", "\\\"").Replace("$", "\\$");

            return $@"#!/bin/bash
set -e

TEMP_FILE=""{escapedTempFile}""
TARGET_PATH=""{escapedTargetPath}""
PARENT_PID={parentPid}
SCRIPT_PATH=""$0""

# Wait for parent process to exit (with timeout)
TIMEOUT=60
ELAPSED=0
while kill -0 $PARENT_PID 2>/dev/null; do
    if [ $ELAPSED -ge $TIMEOUT ]; then
        echo ""Error: Timeout waiting for parent process to exit"" >&2
        rm -f ""$SCRIPT_PATH""
        exit 1
    fi
    sleep 0.5
    ELAPSED=$((ELAPSED + 1))
done

# Replace executable
if [ ! -f ""$TEMP_FILE"" ]; then
    echo ""Error: Temporary file not found: $TEMP_FILE"" >&2
    rm -f ""$SCRIPT_PATH""
    exit 1
fi

mv ""$TEMP_FILE"" ""$TARGET_PATH""

# Set permissions
chmod +x ""$TARGET_PATH""

# Verify new version works
if ""$TARGET_PATH"" --version >/dev/null 2>&1; then
    echo ""Updaemon updated successfully""
else
    echo ""Warning: Updated executable verification failed"" >&2
fi

# Clean up
rm -f ""$SCRIPT_PATH""
";
        }
    }
}

