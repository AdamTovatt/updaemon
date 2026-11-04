using Updaemon.Commands;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Commands
{
    public class SelfUpdateCommandTests
    {
        [Theory]
        [InlineData(new string[0], false)]
        [InlineData(new[] { "on" }, true)]
        [InlineData(new[] { "off" }, true)]
        public void ChangesState_OnlyWhenTurnedOnOrOff(string[] args, bool changes)
        {
            using (AppHarness h = new AppHarness())
            {
                // Showing it takes no lock and prints no scope line.
                Assert.Equal(changes, new SelfUpdateCommand(h.Config, h.Output, h.Scope).ChangesState(args));
            }
        }

        [Fact]
        public async Task SelfUpdate_Off_IsStoredNamingWhatItNoLongerUpdates()
        {
            using (AppHarness h = new AppHarness())
            {
                int exitCode = await new SelfUpdateCommand(h.Config, h.Output, h.Scope).ExecuteAsync(new[] { "off" });

                Assert.Equal(0, exitCode);
                Assert.False((await h.Config.LoadConfigAsync()).SelfUpdate);
                Assert.Equal(new[] { $"Self-update is off: 'updaemon update' no longer updates {h.Scope.UpdaemonCommandPath} and its GitHub plugins." }, h.Output.Messages);
            }
        }

        [Fact]
        public async Task SelfUpdate_OnAfterOff_IsStoredAgain()
        {
            using (AppHarness h = new AppHarness())
            {
                SelfUpdateCommand command = new SelfUpdateCommand(h.Config, h.Output, h.Scope);
                await command.ExecuteAsync(new[] { "off" });

                await command.ExecuteAsync(new[] { "on" });

                Assert.True((await h.Config.LoadConfigAsync()).SelfUpdate);
                Assert.Equal($"Self-update is on: 'updaemon update' also updates {h.Scope.UpdaemonCommandPath} and its GitHub plugins.", h.Output.Messages[^1]);
            }
        }

        [Fact]
        public async Task SelfUpdate_NoArgumentWhenOff_ShowsOff()
        {
            using (AppHarness h = new AppHarness())
            {
                SelfUpdateCommand command = new SelfUpdateCommand(h.Config, h.Output, h.Scope);
                await command.ExecuteAsync(new[] { "off" });

                await command.ExecuteAsync(Array.Empty<string>());

                Assert.Equal("Self-update is off.", h.Output.Messages[^1]);
            }
        }

        [Fact]
        public async Task SelfUpdate_NoArgument_IsOnByDefault()
        {
            using (AppHarness h = new AppHarness())
            {
                await new SelfUpdateCommand(h.Config, h.Output, h.Scope).ExecuteAsync(Array.Empty<string>());

                Assert.Equal(new[] { "Self-update is on." }, h.Output.Messages);
            }
        }

        [Fact]
        public async Task SelfUpdate_OtherArgument_ShowsUsage()
        {
            using (AppHarness h = new AppHarness())
            {
                int exitCode = await new SelfUpdateCommand(h.Config, h.Output, h.Scope).ExecuteAsync(new[] { "maybe" });

                Assert.Equal(1, exitCode);
                Assert.Equal(new[] { "Usage: updaemon self-update [on|off]" }, h.Output.Errors);
            }
        }
    }
}
