using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    public class ServiceDeployerTests
    {
        private static InstalledApp App(string name = "byteshelf") => new InstalledApp { Name = name, Plugin = "github", Remote = "owner/byteshelf" };

        [Fact]
        public async Task StageAsync_InvalidReleaseManifest_ThrowsAndDeletesTheDownload()
        {
            using (AppHarness h = new AppHarness())
            {
                h.AddRelease("owner/byteshelf", "1.0.0", """{ "executable": "bin/byteshelf" }""");

                await Assert.ThrowsAsync<ManifestException>(() => h.Deployer.StageAsync(App(), new Version(1, 0, 0), h.Client));

                Assert.False(Directory.Exists(Path.Combine(h.Scope.GetAppDirectory("byteshelf"), "1.0.0")));
            }
        }

        [Fact]
        public async Task StageAsync_LeftoverDirectoryOfThatVersion_IsReplaced()
        {
            using (AppHarness h = new AppHarness())
            {
                h.AddRelease("owner/byteshelf", "1.0.0", AppHarness.ByteShelfManifest);
                string leftover = Path.Combine(h.Scope.GetAppDirectory("byteshelf"), "1.0.0", "partial");
                Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
                File.WriteAllText(leftover, "from an interrupted attempt");

                DeployableVersion staged = await h.Deployer.StageAsync(App(), new Version(1, 0, 0), h.Client);

                Assert.False(File.Exists(leftover));
                Assert.True(File.Exists(Path.Combine(staged.Directory, "bin", "byteshelf")));
            }
        }

        [Fact]
        public async Task StageAsync_TheCurrentVersion_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                InstalledApp app = (await h.Config.GetAppAsync("byteshelf"))!;

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Deployer.StageAsync(app, new Version(1, 0, 0), h.Client));

                Assert.Contains("already the current version", ex.Message);
                Assert.True(File.Exists(Path.Combine(h.Scope.GetCurrentLinkPath("byteshelf"), "bin", "byteshelf")));
            }
        }

        [Fact]
        public async Task ActivateAsync_ManifestRenamesCommandAndDropsService_RemovesTheOldWrapperAndUnit()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "2.0.0", """{ "executable": "bin/byteshelf", "command": "shelf" }""");
                h.ServiceManager.MethodCalls.Clear();
                InstalledApp app = (await h.Config.GetAppAsync("byteshelf"))!;

                DeployableVersion staged = await h.Deployer.StageAsync(app, new Version(2, 0, 0), h.Client);
                await h.Deployer.ActivateAsync(app, staged, h.Reachable);

                Assert.False(File.Exists(h.Scope.GetCommandPath("byteshelf")));
                Assert.True(File.Exists(h.Scope.GetCommandPath("shelf")));
                Assert.False(File.Exists(h.Scope.GetUnitFilePath("byteshelf")));
                Assert.False(File.Exists(h.Scope.GetServiceWrapperPath("byteshelf")));
                int stop = h.ServiceManager.MethodCalls.IndexOf(MockServiceManager.Call(nameof(IServiceManager.StopServiceAsync), "byteshelf"));
                int disable = h.ServiceManager.MethodCalls.IndexOf(MockServiceManager.Call(nameof(IServiceManager.DisableServiceAsync), "byteshelf"));
                Assert.InRange(stop, 0, disable - 1);
            }
        }

        [Fact]
        public async Task ActivateAsync_CommandNameTakenByAnotherFile_ThrowsAndKeepsCurrentVersion()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                File.WriteAllText(h.Scope.GetCommandPath("shelf"), "#!/bin/sh\necho mine\n");
                h.AddRelease("owner/byteshelf", "2.0.0", """{ "executable": "bin/byteshelf", "command": "shelf" }""");
                InstalledApp app = (await h.Config.GetAppAsync("byteshelf"))!;
                DeployableVersion staged = await h.Deployer.StageAsync(app, new Version(2, 0, 0), h.Client);

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Deployer.ActivateAsync(app, staged, h.Reachable));

                Assert.Contains("did not create it", ex.Message);
                Assert.Equal(new Version(1, 0, 0), await h.Versions.GetCurrentAsync(new AppName("byteshelf")));
                Assert.Equal("#!/bin/sh\necho mine\n", File.ReadAllText(h.Scope.GetCommandPath("shelf")));
            }
        }

        [Fact]
        public async Task ActivateAsync_CommandNameOwnedByAnotherApp_Throws()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/other", "1.0.0", """{ "executable": "bin/byteshelf", "command": "byteshelf" }""");
                InstalledApp other = new InstalledApp { Name = "other", Plugin = "github", Remote = "owner/other" };
                DeployableVersion staged = await h.Deployer.StageAsync(other, new Version(1, 0, 0), h.Client);

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Deployer.ActivateAsync(other, staged, h.Reachable));

                Assert.Equal("The command 'byteshelf' already belongs to the app 'byteshelf'.", ex.Message);
            }
        }

        [Fact]
        public async Task StageAsync_ExecutableMissingFromTheRelease_ThrowsAndDeletesTheDownload()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "2.0.0", """{ "executable": "bin/renamed", "service": {} }""");
                InstalledApp app = (await h.Config.GetAppAsync("byteshelf"))!;

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Deployer.StageAsync(app, new Version(2, 0, 0), h.Client));

                Assert.Contains("'bin/renamed'", ex.Message);
                Assert.Equal(new[] { new Version(1, 0, 0) }, h.Versions.GetRetained(new AppName("byteshelf")));
            }
        }

        [Fact]
        public async Task StageAsync_MakesTheVersionReadyToRun()
        {
            using (AppHarness h = new AppHarness())
            {
                h.AddRelease("owner/byteshelf", "1.0.0", AppHarness.ByteShelfManifest);

                DeployableVersion staged = await h.Deployer.StageAsync(App(), new Version(1, 0, 0), h.Client);

                Assert.Equal(h.Versions.GetVersionDirectory(new AppName("byteshelf"), new Version(1, 0, 0)), staged.Directory);
                Assert.True(File.Exists(staged.ExecutablePath));
                // Before it is a version: a run killed in between leaves none that is not ready.
                string partial = staged.Directory + ".partial";
                Assert.Equal(new[] { Path.Combine(partial, staged.Manifest.Executable) }, h.Permissions.ExecutablePermissionsCalls);
                Assert.Equal(new[] { partial }, h.Permissions.TreesMadeReadable);
            }
        }

        [Fact]
        public async Task ApplySettingsAsync_EnabledServiceCrashLooping_IsRestartedOnTheNewUnitAndChecked()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                // Between two of the service manager's automatic restarts: not running now.
                h.ServiceManager.ServiceRunningStates["byteshelf"] = false;
                h.ServiceManager.MethodCalls.Clear();

                bool staysRunning = await h.Deployer.ApplySettingsAsync(new PreparedApp((await h.Config.GetAppAsync("byteshelf"))!), h.Reachable);

                Assert.True(staysRunning);
                Assert.Contains(MockServiceManager.Call(nameof(IServiceManager.RestartServiceAsync), "byteshelf"), h.ServiceManager.MethodCalls);
                Assert.Contains("Restarted byteshelf.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task ApplySettingsAsync_ServiceDisabled_IsLeftAlone()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                await h.ServiceManager.DisableServiceAsync("byteshelf");
                h.ServiceManager.MethodCalls.Clear();

                Assert.True(await h.Deployer.ApplySettingsAsync(new PreparedApp((await h.Config.GetAppAsync("byteshelf"))!), h.Reachable));

                Assert.DoesNotContain(MockServiceManager.Call(nameof(IServiceManager.RestartServiceAsync), "byteshelf"), h.ServiceManager.MethodCalls);
                Assert.DoesNotContain(MockServiceManager.Call(nameof(IServiceManager.StartServiceAsync), "byteshelf"), h.ServiceManager.MethodCalls);
            }
        }

        [Fact]
        public async Task StartAsync_ServiceRestartedMeanwhile_IsUnhealthy()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.ServiceManager.RestartingServices.Add("byteshelf");

                Assert.False(await h.Deployer.StartAsync("byteshelf", h.Reachable));
            }
        }

        [Fact]
        public async Task StartAsync_ServiceKeptRunning_IsHealthyAfterTheDefaultWindow()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                Assert.True(await h.Deployer.StartAsync("byteshelf", h.Reachable));
                Assert.Equal(TimeSpan.FromSeconds(10), h.Time.Waits[^1]);
            }
        }

        [Fact]
        public async Task StartAsync_ReleaseSetsItsWindow_WaitsThatLong()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/slow", "1.0.0", """{ "executable": "bin/slow", "service": { "healthCheckSeconds": 45 } }""", "bin/slow");

                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "slow", "--from", "github:owner/slow" }));
                Assert.Equal(TimeSpan.FromSeconds(45), h.Time.Waits[^1]);
            }
        }

        [Fact]
        public async Task Update_NewReleaseSetsALongerWindow_TheRestartWaitsTheNewWindow()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "1.1.0", AppHarness.ByteShelfManifest.Replace("\"args\": [\"sync\", \"run\"]", "\"args\": [\"sync\", \"run\"], \"healthCheckSeconds\": 45"));

                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "byteshelf" }));

                Assert.Equal(new Version(1, 1, 0), await h.Versions.GetCurrentAsync(new AppName("byteshelf")));
                Assert.Equal(TimeSpan.FromSeconds(45), h.Time.Waits[^1]);
            }
        }

        [Fact]
        public async Task StartAsync_ServiceStopsDuringTheWait_IsUnhealthy()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.ServiceManager.ScriptedReadings["byteshelf"] = new Queue<bool>(new[] { true, false });

                Assert.False(await h.Deployer.StartAsync("byteshelf", h.Reachable));
            }
        }

        [Fact]
        public async Task StartAsync_ServiceComesUpAMomentAfterStarting_IsHealthy()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.ServiceManager.ScriptedReadings["byteshelf"] = new Queue<bool>(new[] { false, true, true });

                Assert.True(await h.Deployer.StartAsync("byteshelf", h.Reachable));
            }
        }

        [Fact]
        public async Task StartAsync_ServiceNeverComesUp_IsUnhealthyOnceTheWindowIsOver()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.ServiceManager.CrashOnNextStart.Add("byteshelf");
                int waitsBefore = h.Time.Waits.Count;

                Assert.False(await h.Deployer.StartAsync("byteshelf", h.Reachable));
                Assert.Equal(TimeSpan.FromSeconds(10), h.Time.Waits.Skip(waitsBefore).Aggregate(TimeSpan.Zero, (sum, wait) => sum + wait));
            }
        }

        [Fact]
        public async Task RestartAsync_ServiceStopped_RestartsItFromItsUnit()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.ServiceManager.ServiceRunningStates["byteshelf"] = false;
                h.ServiceManager.MethodCalls.Clear();

                Assert.True(await h.Deployer.RestartAsync("byteshelf", h.Reachable));

                // A restart, not a start: on systemd that also clears a unit failed from crashing too often.
                Assert.Contains(MockServiceManager.Call(nameof(IServiceManager.RestartServiceAsync), "byteshelf"), h.ServiceManager.MethodCalls);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RemoveAsync_DeletesEverything_KeepsDataUnlessPurged(bool purge)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                InstalledApp app = (await h.Config.GetAppAsync("byteshelf"))!;

                await h.Deployer.RemoveAsync(app, purge, h.Reachable);

                Assert.False(Directory.Exists(h.Scope.GetAppDirectory("byteshelf")));
                Assert.False(File.Exists(h.Scope.GetCommandPath("byteshelf")));
                Assert.False(File.Exists(h.Scope.GetUnitFilePath("byteshelf")));
                Assert.False(File.Exists(h.Scope.GetSettingsFilePath("byteshelf")));
                Assert.False(File.Exists(h.Scope.GetSecretsFilePath("byteshelf")));
                Assert.Equal(!purge, Directory.Exists(h.Scope.GetDataDirectory("byteshelf")));
            }
        }

        [Fact]
        public async Task RemoveAsync_OnMacOSWithoutALogDirectory_Purges()
        {
            using (AppHarness h = new AppHarness(isMacOS: true))
            {
                await h.InstallByteShelfAsync();
                Assert.False(Directory.Exists(h.Scope.LogDirectory));

                await h.Deployer.RemoveAsync((await h.Config.GetAppAsync("byteshelf"))!, purge: true, h.Reachable);

                Assert.False(Directory.Exists(h.Scope.GetDataDirectory("byteshelf")));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RemoveAsync_OnMacOS_DeletesTheLogFilesOnlyWhenPurged(bool purge)
        {
            using (AppHarness h = new AppHarness(isMacOS: true))
            {
                await h.InstallByteShelfAsync();
                Directory.CreateDirectory(h.Scope.LogDirectory);
                File.WriteAllText(h.Scope.GetOutputLogPath("byteshelf"), "out");
                File.WriteAllText(h.Scope.GetErrorLogPath("byteshelf"), "err");
                File.WriteAllText(h.Scope.GetOutputLogPath("other"), "out");

                await h.Deployer.RemoveAsync((await h.Config.GetAppAsync("byteshelf"))!, purge, h.Reachable);

                Assert.Equal(!purge, File.Exists(h.Scope.GetOutputLogPath("byteshelf")));
                Assert.Equal(!purge, File.Exists(h.Scope.GetErrorLogPath("byteshelf")));
                Assert.True(File.Exists(h.Scope.GetOutputLogPath("other")));
            }
        }
    }
}
