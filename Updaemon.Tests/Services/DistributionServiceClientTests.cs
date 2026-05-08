using Updaemon.Services;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    /// <summary>
    /// Integration tests for the real DistributionServiceClient. These spawn a child
    /// process (the Updaemon.TestPlugin binary) so the Process and named-pipe lifecycle
    /// is exercised end-to-end. Logic-only stubbing via MockDistributionServiceClient
    /// cannot catch lifecycle bugs in the real client.
    /// </summary>
    public class DistributionServiceClientTests
    {
        [Fact]
        public async Task ConnectAsync_TwoConsecutiveCyclesOnSameClient_DoesNotThrow()
        {
            // Regression test: prior to the fix, after the first DisposeAsync the
            // _pluginProcess field was disposed but not nulled. The second ConnectAsync
            // would call HasExited on a disposed Process and throw
            // "No process is associated with this object."
            string pluginPath = TestPluginPathResolver.Resolve();

            DistributionServiceClient client = new DistributionServiceClient();
            try
            {
                await client.ConnectAsync(pluginPath);
                await client.InitializeAsync(null);
                Version? version = await client.GetLatestVersionAsync("first/service");
                Assert.Equal(new Version(1, 0, 0), version);

                await client.DisposeAsync();

                await client.ConnectAsync(pluginPath);
                await client.InitializeAsync(null);
                Version? version2 = await client.GetLatestVersionAsync("second/service");
                Assert.Equal(new Version(1, 0, 0), version2);
            }
            finally
            {
                await client.DisposeAsync();
            }
        }

        [Fact]
        public async Task ConnectAsync_FailedConnectThenSuccessfulConnect_RecoversCleanly()
        {
            // Regression test for the catch block in ConnectAsync that runs
            // CleanupExistingConnectionAsync on failure. After a failed connect,
            // the same instance must be usable for a fresh successful connect.
            string pluginPath = TestPluginPathResolver.Resolve();

            DistributionServiceClient client = new DistributionServiceClient();
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await client.ConnectAsync("/usr/bin/false"));

                await client.ConnectAsync(pluginPath);
                await client.InitializeAsync(null);
                Version? version = await client.GetLatestVersionAsync("recover/service");
                Assert.Equal(new Version(1, 0, 0), version);
            }
            finally
            {
                await client.DisposeAsync();
            }
        }

        [Fact]
        public async Task ConnectAsync_PluginExitsBeforePipeHandshake_ThrowsWithDiagnostics()
        {
            // /usr/bin/false runs and exits with status 1 immediately, so the named
            // pipe never gets a server end. The client should detect the process
            // exit and surface an exception including binary path and exit code
            // rather than hang or print "No process is associated with this object."
            // (Note: /bin/false does not exist on modern macOS; /usr/bin/false works on
            // both macOS and Linux, where /bin is symlinked to /usr/bin under usr-merge.)
            string failingBinary = "/usr/bin/false";

            DistributionServiceClient client = new DistributionServiceClient();
            try
            {
                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await client.ConnectAsync(failingBinary));

                Assert.Contains("exited", ex.Message);
                Assert.Contains("exit code", ex.Message);
                Assert.Contains(failingBinary, ex.Message);
            }
            finally
            {
                await client.DisposeAsync();
            }
        }

        [Fact]
        public async Task ConnectAsync_PluginExitsWithStderr_IncludesStderrInDiagnostic()
        {
            // /bin/sh that writes to stderr then exits non-zero verifies that
            // the stderr-capture branch in ConnectAsync (event handler -> buffer
            // -> diagnostic message) actually gets exercised end-to-end.
            string sentinel = "updaemon_test_stderr_marker_" + Guid.NewGuid().ToString("N");
            string scriptDir = Path.Combine(Path.GetTempPath(), $"updaemon_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(scriptDir);
            string scriptPath = Path.Combine(scriptDir, "fail-with-stderr.sh");
            await File.WriteAllTextAsync(scriptPath, $"#!/bin/sh\necho '{sentinel}' >&2\nexit 7\n");
#pragma warning disable CA1416 // Linux-only test; updaemon targets linux-arm64 systemd hosts.
            File.SetUnixFileMode(scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
#pragma warning restore CA1416

            DistributionServiceClient client = new DistributionServiceClient();
            try
            {
                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await client.ConnectAsync(scriptPath));

                Assert.Contains(sentinel, ex.Message);
                Assert.Contains("exit code 7", ex.Message);
            }
            finally
            {
                await client.DisposeAsync();
                try
                {
                    Directory.Delete(scriptDir, recursive: true);
                }
                catch
                {
                    // Best effort cleanup.
                }
            }
        }
    }
}
