using Updaemon.Interfaces;
using Updaemon.Models;
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
                await h.Deployer.ActivateAsync(app, staged);

                Assert.False(File.Exists(h.Scope.GetCommandPath("byteshelf")));
                Assert.True(File.Exists(h.Scope.GetCommandPath("shelf")));
                Assert.False(File.Exists(h.Scope.GetUnitFilePath("byteshelf")));
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

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Deployer.ActivateAsync(app, staged));

                Assert.Contains("did not create it", ex.Message);
                Assert.Equal(new Version(1, 0, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
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

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Deployer.ActivateAsync(other, staged));

                Assert.Equal("The command 'byteshelf' already belongs to the app 'byteshelf'.", ex.Message);
            }
        }

        [Fact]
        public async Task ActivateAsync_ExecutableMissingFromTheRelease_ThrowsAndKeepsCurrentVersion()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/byteshelf", "2.0.0", """{ "executable": "bin/renamed", "service": {} }""");
                InstalledApp app = (await h.Config.GetAppAsync("byteshelf"))!;
                DeployableVersion staged = await h.Deployer.StageAsync(app, new Version(2, 0, 0), h.Client);

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Deployer.ActivateAsync(app, staged));

                Assert.Contains("'bin/renamed'", ex.Message);
                Assert.Equal(new Version(1, 0, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
            }
        }

        [Fact]
        public async Task ActivateAsync_GeneratingFilesFails_SwitchesBackToThePreviousVersion()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/byteshelf", "1.0.0", """{ "executable": "bin/byteshelf", "command": "byteshelf" }""");
                await h.Install().ExecuteAsync(new[] { "byteshelf", "--from", "github:owner/byteshelf" });
                h.AddRelease("owner/byteshelf", "2.0.0", """{ "executable": "bin/byteshelf", "command": "byteshelf", "service": {} }""");
                InstalledApp app = (await h.Config.GetAppAsync("byteshelf"))!;
                DeployableVersion staged = await h.Deployer.StageAsync(app, new Version(2, 0, 0), h.Client);
                // A file where the unit directory should be: writing the new service unit fails.
                File.WriteAllText(h.Scope.UnitDirectory, "");

                await Assert.ThrowsAnyAsync<IOException>(() => h.Deployer.ActivateAsync(app, staged));

                Assert.Equal(new Version(1, 0, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
            }
        }

        [Fact]
        public async Task ApplySettingsAsync_ServiceStopped_DoesNotStartIt()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.ServiceManager.ServiceRunningStates["byteshelf"] = false;
                h.ServiceManager.MethodCalls.Clear();

                await h.Deployer.ApplySettingsAsync((await h.Config.GetAppAsync("byteshelf"))!);

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

                Assert.False(await h.Deployer.StartAsync("byteshelf"));
            }
        }

        [Fact]
        public async Task StartAsync_ServiceKeptRunning_IsHealthyAfterTheDefaultWindow()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                Assert.True(await h.Deployer.StartAsync("byteshelf"));
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

                Assert.Equal(new Version(1, 1, 0), await h.Deployer.GetCurrentVersionAsync("byteshelf"));
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

                Assert.False(await h.Deployer.StartAsync("byteshelf"));
            }
        }

        [Fact]
        public async Task StartAsync_ServiceComesUpAMomentAfterStarting_IsHealthy()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.ServiceManager.ScriptedReadings["byteshelf"] = new Queue<bool>(new[] { false, true, true });

                Assert.True(await h.Deployer.StartAsync("byteshelf"));
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

                Assert.False(await h.Deployer.StartAsync("byteshelf"));
                Assert.Equal(TimeSpan.FromSeconds(10), h.Time.Waits.Skip(waitsBefore).Aggregate(TimeSpan.Zero, (sum, wait) => sum + wait));
            }
        }

        [Fact]
        public async Task RestartAsync_ServiceStopped_StartsIt()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.ServiceManager.ServiceRunningStates["byteshelf"] = false;
                h.ServiceManager.MethodCalls.Clear();

                await h.Deployer.RestartAsync("byteshelf");

                Assert.Contains(MockServiceManager.Call(nameof(IServiceManager.StartServiceAsync), "byteshelf"), h.ServiceManager.MethodCalls);
                Assert.DoesNotContain(MockServiceManager.Call(nameof(IServiceManager.RestartServiceAsync), "byteshelf"), h.ServiceManager.MethodCalls);
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

                await h.Deployer.RemoveAsync(app, purge);

                Assert.False(Directory.Exists(h.Scope.GetAppDirectory("byteshelf")));
                Assert.False(File.Exists(h.Scope.GetCommandPath("byteshelf")));
                Assert.False(File.Exists(h.Scope.GetUnitFilePath("byteshelf")));
                Assert.False(File.Exists(h.Scope.GetSettingsFilePath("byteshelf")));
                Assert.False(File.Exists(h.Scope.GetSecretsFilePath("byteshelf")));
                Assert.Equal(!purge, Directory.Exists(h.Scope.GetDataDirectory("byteshelf")));
            }
        }

        [Fact]
        public async Task PruneOldVersionsAsync_KeepsNewestAndCurrent_IgnoresOtherDirectories()
        {
            using (AppHarness h = new AppHarness())
            {
                string appDirectory = h.Scope.GetAppDirectory("my-api");
                foreach (string version in new[] { "1.0.0", "1.1.0", "1.2.0", "1.3.0", "not-a-version" })
                {
                    Directory.CreateDirectory(Path.Combine(appDirectory, version));
                }

                File.CreateSymbolicLink(h.Scope.GetCurrentLinkPath("my-api"), Path.Combine(appDirectory, "1.0.0"));

                await h.Deployer.PruneOldVersionsAsync("my-api", 2);

                string[] remaining = Directory.GetDirectories(appDirectory).Select(Path.GetFileName).Order().ToArray()!;
                Assert.Equal(new[] { "1.0.0", "1.2.0", "1.3.0", "current", "not-a-version" }, remaining);
            }
        }
    }
}
