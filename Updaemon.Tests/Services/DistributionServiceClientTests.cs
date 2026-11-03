using Updaemon.Common;
using Updaemon.Common.Models;
using Updaemon.Configuration;
using Updaemon.Models;
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
        private static readonly HostPlatform TestPlatform = new HostPlatform(false, "linux-arm64");

        [Fact]
        public async Task DownloadVersionAsync_PassesTheRuntimeIdentifierToThePlugin()
        {
            string pluginPath = TestPluginPathResolver.Resolve();
            string targetPath = Path.Combine(Path.GetTempPath(), $"updaemon_test_{Guid.NewGuid():N}");

            DistributionServiceClient client = new DistributionServiceClient(new HostPlatform(true, "osx-arm64"));
            try
            {
                await client.ConnectAsync(pluginPath);
                await client.DownloadVersionAsync("owner/repo", new Version(1, 0, 0), targetPath);

                // The test plugin writes one file named after the runtime identifier it received.
                Assert.Equal("osx-arm64", Path.GetFileName(Assert.Single(Directory.GetFiles(targetPath))));
            }
            finally
            {
                await client.DisposeAsync();
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, recursive: true);
                }
            }
        }

        [Theory]
        [InlineData("owner/repo", true)]
        [InlineData("unverified", false)]
        public async Task DownloadVersionAsync_ReturnsWhetherThePluginVerifiedTheDownload(string remote, bool expected)
        {
            string pluginPath = TestPluginPathResolver.Resolve();
            string targetPath = Path.Combine(Path.GetTempPath(), $"updaemon_test_{Guid.NewGuid():N}");

            DistributionServiceClient client = new DistributionServiceClient(TestPlatform);
            try
            {
                await client.ConnectAsync(pluginPath);
                DownloadResult result = await client.DownloadVersionAsync(remote, new Version(1, 0, 0), targetPath);

                Assert.Equal(expected, result.Verified);
            }
            finally
            {
                await client.DisposeAsync();
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, recursive: true);
                }
            }
        }

        [Fact]
        public async Task DownloadVersionAsync_PluginReportsNoResult_Fails()
        {
            string pluginPath = TestPluginPathResolver.Resolve();
            string targetPath = Path.Combine(Path.GetTempPath(), $"updaemon_test_{Guid.NewGuid():N}");

            DistributionServiceClient client = new DistributionServiceClient(TestPlatform);
            try
            {
                await client.ConnectAsync(pluginPath);

                PluginException exception = await Assert.ThrowsAsync<PluginException>(
                    () => client.DownloadVersionAsync("no-result", new Version(1, 0, 0), targetPath));

                Assert.Equal("the plugin's download reported no result, which its protocol version requires.", exception.Message);
            }
            finally
            {
                await client.DisposeAsync();
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, recursive: true);
                }
            }
        }

        [Fact]
        public async Task PluginError_ReachesTheCallerAsThePluginsMessageAlone()
        {
            string pluginPath = TestPluginPathResolver.Resolve();

            DistributionServiceClient client = new DistributionServiceClient(TestPlatform);
            try
            {
                await client.ConnectAsync(pluginPath);

                PluginException exception = await Assert.ThrowsAsync<PluginException>(
                    () => client.GetLatestVersionAsync("fail"));

                Assert.Equal("Remote 'fail' does not exist. Check the remote name.", exception.Message);
            }
            finally
            {
                await client.DisposeAsync();
            }
        }

        [Fact]
        public async Task PluginErrorWithoutMessage_SaysWhichCallFailed()
        {
            string pluginPath = TestPluginPathResolver.Resolve();

            DistributionServiceClient client = new DistributionServiceClient(TestPlatform);
            try
            {
                await client.ConnectAsync(pluginPath);

                PluginException exception = await Assert.ThrowsAsync<PluginException>(
                    () => client.GetLatestVersionAsync("fail-silently"));

                Assert.Equal("The plugin failed GetLatestVersionAsync without saying why.", exception.Message);
            }
            finally
            {
                await client.DisposeAsync();
            }
        }

        [Fact]
        public void ReadServiceInformation_NoInformation_RefusesThePlugin()
        {
            PluginException exception = Assert.Throws<PluginException>(
                () => DistributionServiceClient.ReadServiceInformation(null, "/plugins/github/plugin"));

            Assert.Equal("The plugin at /plugins/github/plugin returned no service information.", exception.Message);
        }

        [Fact]
        public void ReadServiceInformation_CurrentProtocol_ReturnsTheInformation()
        {
            string json = $"{{\"fullName\":\"GitHub Releases\",\"defaultAlias\":\"github\",\"version\":\"1.0.0\",\"secrets\":[],\"protocolVersion\":{PluginProtocol.Version}}}";

            DistributionServiceInformation information = DistributionServiceClient.ReadServiceInformation(json, "/plugins/github/plugin");

            Assert.Equal("github", information.DefaultAlias);
        }

        [Theory]
        [InlineData("", 1)]
        [InlineData(",\"protocolVersion\":3", 3)]
        [InlineData(",\"protocolVersion\":\"2\"", 1)]
        public void ReadServiceInformation_OtherProtocol_RefusesThePluginAndSaysToReinstallIt(string protocolProperty, int reportedVersion)
        {
            string json = $"{{\"fullName\":\"GitHub Releases\",\"defaultAlias\":\"github\",\"version\":\"0.4.0\",\"secrets\":[]{protocolProperty}}}";

            PluginProtocolException exception = Assert.Throws<PluginProtocolException>(
                () => DistributionServiceClient.ReadServiceInformation(json, "/plugins/github/plugin"));

            Assert.Equal(
                $"The plugin at /plugins/github/plugin speaks plugin protocol {reportedVersion}, but this updaemon speaks protocol {PluginProtocol.Version}. " +
                "Reinstall the plugin with 'updaemon dist-install [--as <alias>] <plugin-name|github:owner/repo|url>'.",
                exception.Message);
        }

        [Fact]
        public async Task ConnectAsync_TwoConsecutiveCyclesOnSameClient_DoesNotThrow()
        {
            // Regression test: prior to the fix, after the first DisposeAsync the
            // _pluginProcess field was disposed but not nulled. The second ConnectAsync
            // would call HasExited on a disposed Process and throw
            // "No process is associated with this object."
            string pluginPath = TestPluginPathResolver.Resolve();

            DistributionServiceClient client = new DistributionServiceClient(TestPlatform);
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

            DistributionServiceClient client = new DistributionServiceClient(TestPlatform);
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

            DistributionServiceClient client = new DistributionServiceClient(TestPlatform);
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

            DistributionServiceClient client = new DistributionServiceClient(TestPlatform);
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
