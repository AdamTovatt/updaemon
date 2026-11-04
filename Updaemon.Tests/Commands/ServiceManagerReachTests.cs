using Updaemon.Models;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Commands
{
    /// <summary>Without a user service manager, what needs it writes nothing; what does not, works.</summary>
    public class ServiceManagerReachTests
    {
        private const string Unreachable = "Cannot reach systemd's user manager for ada.";

        [Fact]
        public async Task Install_Service_WritesNothing()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/byteshelf", "1.0.0", AppHarness.ByteShelfManifest);
                h.ServiceManager.Unreachable = Unreachable;
                // The scope's own directory for apps, which stays once made, is not the app's.
                Directory.CreateDirectory(h.Scope.AppsDirectory);
                IReadOnlyList<string> before = h.Snapshot();

                ServiceManagerUnreachableException ex = await Assert.ThrowsAsync<ServiceManagerUnreachableException>(() => h.Install().ExecuteAsync(new[]
                {
                    "byteshelf", "--from", "github:owner/byteshelf", "--set", "BYTESHELF_URL=https://shelf", "--set", "BYTESHELF_API_KEY=key1",
                }));

                Assert.Equal(Unreachable, ex.Message);
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Install_CommandOnly_DoesNotNeedIt()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/rg", "1.0.0", """{ "executable": "bin/byteshelf", "command": "rg" }""");
                h.ServiceManager.Unreachable = Unreachable;

                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "rg", "--from", "github:owner/rg" }));
                Assert.True(File.Exists(h.Scope.GetCommandPath("rg")));
            }
        }

        public static TheoryData<string, Func<AppHarness, Task<int>>> CommandsOnAService => new TheoryData<string, Func<AppHarness, Task<int>>>
        {
            { "set", h => h.Set().ExecuteAsync(new[] { "byteshelf", "BYTESHELF_LOG_LEVEL=debug" }) },
            { "unset", h => h.Unset().ExecuteAsync(new[] { "byteshelf", "BYTESHELF_LOG_LEVEL" }) },
            { "set-args", h => h.SetArgs().ExecuteAsync(new[] { "byteshelf", "--", "serve" }) },
            { "remove", h => h.Remove().ExecuteAsync(new[] { "byteshelf" }) },
            { "rollback", h => h.RollbackCommand().ExecuteAsync(new[] { "byteshelf" }) },
        };

        [Theory]
        [MemberData(nameof(CommandsOnAService))]
        public async Task CommandOnAService_WritesNothing(string command, Func<AppHarness, Task<int>> run)
        {
            _ = command; // Names the case in the test's display name.
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfPastACrashingReleaseAsync();
                await h.Set().ExecuteAsync(new[] { "byteshelf", "BYTESHELF_LOG_LEVEL=warn" });
                h.ServiceManager.Unreachable = Unreachable;
                IReadOnlyList<string> before = h.Snapshot();

                await Assert.ThrowsAsync<ServiceManagerUnreachableException>(() => run(h));

                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Update_ServiceApp_FailsThatAppAndWritesNothingForIt()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "2.0.0", AppHarness.ByteShelfManifest);
                h.ServiceManager.Unreachable = Unreachable;
                IReadOnlyList<string> before = h.Snapshot();

                int exitCode = await h.Update().ExecuteAsync(new[] { "byteshelf" });

                Assert.Equal(1, exitCode);
                Assert.Contains($"Error: byteshelf: {Unreachable}", h.Output.Errors);
                h.AssertUnchangedSince(before);
            }
        }

        private const string CommandOnlyManifest = """{ "executable": "bin/byteshelf", "command": "rg", "settings": { "LEVEL": { "default": "info" } } }""";

        public static TheoryData<string, Func<AppHarness, Task<int>>> CommandsOnACommandOnlyApp => new TheoryData<string, Func<AppHarness, Task<int>>>
        {
            { "set", h => h.Set().ExecuteAsync(new[] { "rg", "LEVEL=debug" }) },
            { "unset", h => h.Unset().ExecuteAsync(new[] { "rg", "LEVEL" }) },
            { "remove", h => h.Remove().ExecuteAsync(new[] { "rg" }) },
            { "rollback", h => h.RollbackCommand().ExecuteAsync(new[] { "rg" }) },
            { "update", h => h.Update().ExecuteAsync(new[] { "rg" }) },
        };

        [Theory]
        [MemberData(nameof(CommandsOnACommandOnlyApp))]
        public async Task CommandOnACommandOnlyApp_DoesNotNeedIt(string command, Func<AppHarness, Task<int>> run)
        {
            _ = command; // Names the case in the test's display name.
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/rg", "1.0.0", CommandOnlyManifest);
                await h.Install().ExecuteAsync(new[] { "rg", "--from", "github:owner/rg" });
                h.AddRelease("owner/rg", "1.1.0", CommandOnlyManifest);
                await h.Update().ExecuteAsync(new[] { "rg" });
                await h.Set().ExecuteAsync(new[] { "rg", "LEVEL=warn" });
                h.AddRelease("owner/rg", "1.2.0", CommandOnlyManifest);
                h.ServiceManager.Unreachable = Unreachable;
                h.Output.Clear();

                int exitCode = await run(h);

                Assert.Empty(h.Output.Errors);
                Assert.Equal(0, exitCode);
                Assert.Equal(0, h.ServiceManager.ReachChecks);
            }
        }
    }
}
