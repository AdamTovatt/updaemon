using Updaemon.Services;
using Xunit;

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
        private static string GetTestPluginPath()
        {
            string testAssemblyDir = AppContext.BaseDirectory;
            DirectoryInfo? net = new DirectoryInfo(testAssemblyDir);
            DirectoryInfo? config = net.Parent;
            DirectoryInfo? bin = config?.Parent;
            DirectoryInfo? testProjectDir = bin?.Parent;
            DirectoryInfo? solutionRoot = testProjectDir?.Parent;

            if (solutionRoot == null || config == null)
            {
                throw new InvalidOperationException($"Unable to locate solution root from test base directory: {testAssemblyDir}");
            }

            string pluginPath = Path.Combine(
                solutionRoot.FullName,
                "Updaemon.TestPlugin",
                "bin",
                config.Name,
                "net8.0",
                "Updaemon.TestPlugin");

            if (!File.Exists(pluginPath))
            {
                throw new FileNotFoundException(
                    $"Test plugin not found at '{pluginPath}'. Ensure Updaemon.TestPlugin builds before running these tests.");
            }

            return pluginPath;
        }

        [Fact]
        public async Task ConnectAsync_TwoConsecutiveCyclesOnSameClient_DoesNotThrow()
        {
            // Regression test: prior to the fix, after the first DisposeAsync the
            // _pluginProcess field was disposed but not nulled. The second ConnectAsync
            // would call HasExited on a disposed Process and throw
            // "No process is associated with this object."
            string pluginPath = GetTestPluginPath();

            DistributionServiceClient client = new DistributionServiceClient();
            try
            {
                await client.ConnectAsync(pluginPath);
                await client.InitializeAsync(null);
                Version? version = await client.GetLatestVersionAsync("first/service");
                Assert.NotNull(version);

                await client.DisposeAsync();

                await client.ConnectAsync(pluginPath);
                await client.InitializeAsync(null);
                Version? version2 = await client.GetLatestVersionAsync("second/service");
                Assert.NotNull(version2);
            }
            finally
            {
                await client.DisposeAsync();
            }
        }

        [Fact]
        public async Task ConnectAsync_PluginExitsBeforePipeHandshake_ThrowsWithDiagnostics()
        {
            // Regression test for the diagnostic added alongside the lifecycle fix.
            // /bin/false exists, runs, and exits with status 1 immediately - so the
            // named pipe never gets a server end. The client should detect the
            // process exit and surface a useful error rather than hang or print
            // "No process is associated with this object."
            string failingBinary = "/bin/false";
            if (!File.Exists(failingBinary))
            {
                return;
            }

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
    }
}
