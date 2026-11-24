using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;
using static Updaemon.Tests.Helpers.ShelfPackages;

namespace Updaemon.Tests.Services
{
    /// <summary>Changing settings and arguments: what set, unset and set-args share.</summary>
    public class SettingsChangerTests
    {
        private static async Task<bool> ApplyAsync(AppHarness h) =>
            await h.SettingsChanger.ApplyAsync(await h.SettingsChanger.BeginAsync((await h.Config.GetAppAsync("byteshelf"))!));

        [Fact]
        public async Task ApplyAsync_EnabledServiceCrashLooping_IsRestartedOnTheNewUnitAndChecked()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                // Between two of the service manager's automatic restarts: not running now.
                h.ServiceManager.ServiceRunningStates["byteshelf"] = false;
                h.ServiceManager.MethodCalls.Clear();

                bool staysRunning = await ApplyAsync(h);

                Assert.True(staysRunning);
                Assert.Contains(MockServiceManager.Call(nameof(IServiceManager.RestartServiceAsync), "byteshelf"), h.ServiceManager.MethodCalls);
                Assert.Contains("Restarted byteshelf.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task ApplyAsync_ServiceDisabled_IsLeftAlone()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                await h.ServiceManager.DisableServiceAsync("byteshelf");
                h.ServiceManager.MethodCalls.Clear();

                Assert.True(await ApplyAsync(h));

                Assert.DoesNotContain(MockServiceManager.Call(nameof(IServiceManager.RestartServiceAsync), "byteshelf"), h.ServiceManager.MethodCalls);
                Assert.DoesNotContain(MockServiceManager.Call(nameof(IServiceManager.StartServiceAsync), "byteshelf"), h.ServiceManager.MethodCalls);
            }
        }

        [Fact]
        public async Task Set_RestartFails_TheNextChangeRestartsIt()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.ServiceManager.RestartFailure = new InvalidOperationException("no restart today");
                await Assert.ThrowsAsync<InvalidOperationException>(() => h.Set().ExecuteAsync(new[] { "shelf", "A=1" }));
                Assert.Equal("1", (await h.HostAsync()).Settings["A"]);
                h.ServiceManager.RestartFailure = null;
                h.ForgetCalls();

                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                Assert.Contains("shelf: an earlier run did not finish restarting the service; restarting it.", h.Output.Messages);
                Assert.Equal(1, h.HostRestarts());
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        /// <summary>Without a restart there is none to make up for either.</summary>
        [Fact]
        public async Task Set_OfADisabledServiceCarriedOutCannotBeRecorded_WarnsOfItsFilesOnly()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                await h.ServiceManager.DisableServiceAsync("shelf");
                h.SavedConfig.FailNextWithNothingLeft();

                Assert.Equal(0, await h.Set().ExecuteAsync(new[] { "shelf", "A=1" }));

                Assert.Equal(new[] { "Warning: the next change to shelf writes its files once more: disk full" }, h.Output.Errors);
                Assert.Equal(new UnfinishedChange.CarryOut { Restart = false }, (await h.HostAsync()).Unfinished);
            }
        }

        /// <summary>A change of plain settings, or of none, has nothing to write there.</summary>
        [Fact]
        public async Task ApplyAsync_SecretsUnchanged_DoesNotWriteThem()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                int saves = 0;
                h.SavedSecrets.OnSave = () => saves++;

                Assert.Equal(0, await h.Set().ExecuteAsync(new[] { "byteshelf", "A=1" }));
                Assert.Equal(0, saves);
                Assert.Equal(0, await h.Set().ExecuteAsync(new[] { "byteshelf", "BYTESHELF_API_KEY=key2" }));
                Assert.Equal(1, saves);
                Assert.Equal(0, await h.Set().ExecuteAsync(new[] { "byteshelf", "B=2", "--secret" }));
                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "byteshelf", "B" }));
                Assert.Equal(3, saves);
                Assert.Equal(new Dictionary<string, string> { ["BYTESHELF_API_KEY"] = "key2" }, await h.Secrets.GetAppSecretsAsync("byteshelf"));
            }
        }

        [Fact]
        public async Task Set_CancelledDuringTheHealthCheck_LeavesNothingToMakeUpFor()
        {
            using (AppHarness h = new AppHarness())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                await h.InstallHostAsync();
                h.ServiceManager.OnRestart = cancellation.Cancel;

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Set().ExecuteAsync(new[] { "shelf", "A=1" }, cancellation.Token));

                Assert.Equal("1", (await h.HostAsync()).Settings["A"]);
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        /// <summary>It is applied; only the note that it is not could not be taken away.</summary>
        [Fact]
        public async Task Set_CarriedOutCannotBeRecorded_WarnsAndSucceeds()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.SavedConfig.FailNextWithNothingLeft();

                Assert.Equal(0, await h.Set().ExecuteAsync(new[] { "shelf", "A=1" }));

                Assert.Equal(new[] { "Warning: the next change to shelf restarts it once more: disk full" }, h.Output.Errors);
                Assert.IsType<UnfinishedChange.CarryOut>((await h.HostAsync()).Unfinished);
            }
        }
    }
}
