using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class RollbackCommandTests
    {
        /// <summary>ByteShelf installed at 1.0.0 and updated to 1.1.0, both kept.</summary>
        private static async Task<AppHarness> UpdatedByteShelfAsync()
        {
            AppHarness h = new AppHarness();
            await h.InstallByteShelfAsync();
            h.AddRelease("owner/byteshelf", "1.1.0", AppHarness.ByteShelfManifest);
            await h.Update().ExecuteAsync(Array.Empty<string>());
            h.Output.Clear();
            h.ServiceManager.MethodCalls.Clear();
            return h;
        }

        [Fact]
        public async Task Rollback_GoesToThePreviousKeptVersion_SkipsTheOneRolledBackFrom_AndRestarts()
        {
            using (AppHarness h = await UpdatedByteShelfAsync())
            {
                int exitCode = await h.RollbackCommand().ExecuteAsync(new[] { "byteshelf" });

                Assert.Equal(0, exitCode);
                Assert.Equal(new Version(1, 0, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.Equal(new[] { new Version(1, 1, 0) }, (await h.Config.GetAppAsync("byteshelf"))!.SkippedVersions);
                Assert.Contains(MockServiceManager.Call(nameof(IServiceManager.RestartServiceAsync), "byteshelf"), h.ServiceManager.MethodCalls);
                Assert.Contains("Rolled back byteshelf from 1.1.0 to 1.0.0. 'updaemon update' skips 1.1.0 from now on.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task Rollback_ToANewerSkippedVersion_SwitchesForwardAndSkipsNothing()
        {
            using (AppHarness h = await UpdatedByteShelfAsync())
            {
                await h.RollbackCommand().ExecuteAsync(new[] { "byteshelf" });
                h.Output.Clear();

                int exitCode = await h.RollbackCommand().ExecuteAsync(new[] { "byteshelf", "1.1.0" });

                Assert.Equal(0, exitCode);
                Assert.Equal(new Version(1, 1, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.Empty((await h.Config.GetAppAsync("byteshelf"))!.SkippedVersions);
                Assert.Equal(new[] { "Switched byteshelf from 1.0.0 to 1.1.0." }, h.Output.Messages);
            }
        }

        [Fact]
        public async Task Rollback_NamingAnOlderSkippedVersion_GoesThereAndUnskipsIt()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfPastACrashingReleaseAsync();

                int exitCode = await h.RollbackCommand().ExecuteAsync(new[] { "byteshelf", "1.1.0" });

                Assert.Equal(0, exitCode);
                Assert.Equal(new Version(1, 1, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.Equal(new[] { new Version(1, 2, 0) }, (await h.Config.GetAppAsync("byteshelf"))!.SkippedVersions);
            }
        }

        [Fact]
        public async Task Rollback_VersionNamedWithoutPatch_FindsTheKeptOne()
        {
            using (AppHarness h = await UpdatedByteShelfAsync())
            {
                int exitCode = await h.RollbackCommand().ExecuteAsync(new[] { "byteshelf", "1.0" });

                Assert.Equal(0, exitCode);
                Assert.Equal(new Version(1, 0, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.Equal(new[] { new Version(1, 1, 0) }, (await h.Config.GetAppAsync("byteshelf"))!.SkippedVersions);
            }
        }

        [Fact]
        public async Task Rollback_NamingTheCurrentVersion_Fails()
        {
            using (AppHarness h = await UpdatedByteShelfAsync())
            {
                int exitCode = await h.RollbackCommand().ExecuteAsync(new[] { "byteshelf", "1.1.0" });

                Assert.Equal(1, exitCode);
                Assert.Equal(new[] { "Error: byteshelf 1.1.0 is already the current version." }, h.Output.Errors);
            }
        }

        [Fact]
        public async Task Rollback_TargetDoesNotStayRunning_FailsPointingToTheLog()
        {
            using (AppHarness h = await UpdatedByteShelfAsync())
            {
                h.ServiceManager.CrashOnNextStart.Add("byteshelf");

                int exitCode = await h.RollbackCommand().ExecuteAsync(new[] { "byteshelf" });

                Assert.Equal(1, exitCode);
                Assert.Equal(new Version(1, 0, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.Equal(new[] { "Error: byteshelf: 1.0.0 did not stay running. See 'updaemon logs byteshelf'." }, h.Output.Errors);
            }
        }

        [Fact]
        public async Task Rollback_CommandOnlyApp_SwitchesWithoutStartingAnything()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/tool", "1.0.0", """{ "executable": "tool", "command": "tool" }""", executable: "tool");
                await h.Install().ExecuteAsync(new[] { "tool", "--from", "github:owner/tool" });
                h.AddRelease("owner/tool", "1.1.0", """{ "executable": "tool", "command": "tool" }""", executable: "tool");
                await h.Update().ExecuteAsync(Array.Empty<string>());
                h.ServiceManager.MethodCalls.Clear();

                int exitCode = await h.RollbackCommand().ExecuteAsync(new[] { "tool" });

                Assert.Equal(0, exitCode);
                Assert.Equal(new Version(1, 0, 0), await h.Deployer.GetCurrentVersionAsync("tool"));
                Assert.Empty(h.ServiceManager.MethodCalls);
            }
        }

        [Fact]
        public async Task Rollback_VersionNotKept_FailsListingTheKeptOnes()
        {
            using (AppHarness h = await UpdatedByteShelfAsync())
            {
                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => h.RollbackCommand().ExecuteAsync(new[] { "byteshelf", "0.9.0" }));

                Assert.Equal("byteshelf 0.9.0 is not kept. Kept versions: 1.0.0, 1.1.0.", ex.Message);
                Assert.Equal(new Version(1, 1, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
            }
        }

        [Fact]
        public async Task Rollback_NoOlderVersionKept_Fails()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                int exitCode = await h.RollbackCommand().ExecuteAsync(new[] { "byteshelf" });

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.StartsWith("Error: No version of byteshelf older than 1.0.0 is kept"));
            }
        }

        [Fact]
        public async Task Rollback_PassesOverAnOlderVersionThatFailedItsHealthCheck()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfPastACrashingReleaseAsync();

                int exitCode = await h.RollbackCommand().ExecuteAsync(new[] { "byteshelf" });

                Assert.Equal(0, exitCode);
                Assert.Equal(new Version(1, 0, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.Equal(new[] { new Version(1, 1, 0), new Version(1, 2, 0) }, (await h.Config.GetAppAsync("byteshelf"))!.SkippedVersions);
            }
        }

        [Fact]
        public async Task Rollback_TargetNeedsASettingWithoutValue_ChangesNothingAndNamesTheSetCommand()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                string needsDevice = """{ "executable": "bin/byteshelf", "service": {}, "settings": { "DEVICE": { "required": true } } }""";
                h.AddRelease("owner/byteshelf", "1.0.0", needsDevice);
                await h.Install().ExecuteAsync(new[] { "byteshelf", "--from", "github:owner/byteshelf", "--set", "DEVICE=d" });
                h.AddRelease("owner/byteshelf", "1.1.0", """{ "executable": "bin/byteshelf", "service": {} }""");
                await h.Update().ExecuteAsync(Array.Empty<string>());
                await h.Unset().ExecuteAsync(new[] { "byteshelf", "DEVICE" });

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => h.RollbackCommand().ExecuteAsync(new[] { "byteshelf" }));

                Assert.Equal("1.0.0 needs DEVICE; 1.1.0 keeps running. Run 'updaemon set byteshelf DEVICE=<value>', then 'updaemon rollback byteshelf 1.0.0'.", ex.Message);
                Assert.Equal(new Version(1, 1, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
                Assert.Empty((await h.Config.GetAppAsync("byteshelf"))!.SkippedVersions);
            }
        }
    }
}
