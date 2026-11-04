using Updaemon.Commands;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Commands
{
    public class SetRestartCommandTests
    {
        [Theory]
        [InlineData("manual", false)]
        [InlineData("AUTO", true)]
        public async Task ExecuteAsync_StoresTheMode(string mode, bool expectedAutoRestart)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                SetRestartCommand command = new SetRestartCommand(h.Config, h.Output, h.Deployer);

                int exitCode = await command.ExecuteAsync(new[] { "byteshelf", mode });

                Assert.Equal(0, exitCode);
                Assert.Equal(expectedAutoRestart, (await h.Config.GetAppAsync("byteshelf"))!.AutoRestart);
                Assert.DoesNotContain(h.Output.Messages, m => m.Contains("no effect"));
            }
        }

        [Theory]
        [InlineData("byteshelf", "sometimes", "Invalid restart mode")]
        [InlineData("missing", "manual", "not installed")]
        [InlineData("byteshelf", null, "Usage: updaemon set-restart")]
        public async Task ExecuteAsync_BadInput_FailsWithoutChangingAnything(string app, string? mode, string expectedError)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                SetRestartCommand command = new SetRestartCommand(h.Config, h.Output, h.Deployer);

                int exitCode = await command.ExecuteAsync(mode == null ? new[] { app } : new[] { app, mode });

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains(expectedError));
                Assert.True((await h.Config.GetAppAsync("byteshelf"))!.AutoRestart);
            }
        }

        [Fact]
        public async Task ExecuteAsync_AppWithoutService_NotesTheSettingHasNoEffect()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/tool", "1.0.0", """{ "executable": "tool", "command": "tool" }""", executable: "tool");
                await h.Install().ExecuteAsync(new[] { "tool", "--from", "github:owner/tool" });

                int exitCode = await new SetRestartCommand(h.Config, h.Output, h.Deployer).ExecuteAsync(new[] { "tool", "manual" });

                Assert.Equal(0, exitCode);
                Assert.Contains(h.Output.Messages, m => m.Contains("no effect"));
            }
        }
    }
}
