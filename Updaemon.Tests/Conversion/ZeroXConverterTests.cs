using Updaemon.Conversion;
using Updaemon.Interfaces;
using Updaemon.IO;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Conversion
{
    /// <summary>
    /// Converting what updaemon 0.x left (see <see cref="ZeroXLayout"/>) in a temp system scope.
    /// </summary>
    public class ZeroXConverterTests
    {
        private static readonly ZeroXTimer HourlyTimer = new ZeroXTimer(new[] { "/units/com.updaemon.timer.plist" }, IsOn: true, TimeSpan.FromHours(1));

        private static AppHarness ZeroXSetup(string serviceLines = ZeroXLayout.Settings)
        {
            AppHarness h = new AppHarness(isSystem: true);
            ZeroXLayout.Write(h, serviceLines);
            return h;
        }

        private static string Backup(AppHarness h, string fileName) => Path.Combine(h.Scope.ZeroXBackupDirectory, fileName);

        /// <summary>Writes a drop-in of my-api's 0.x unit, as <c>systemctl edit</c> would, and returns its path.</summary>
        private static string DropIn(AppHarness h, string fileName, string text)
        {
            string path = Path.Combine(h.Scope.UnitDirectory, "my-api.service.d", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }

        /// <summary>A 0.x timer of two files, as on Linux: each is kept in the backup.</summary>
        private static ZeroXTimer TimerIn(AppHarness h, bool isOn, TimeSpan? interval)
        {
            Directory.CreateDirectory(h.Scope.UnitDirectory);
            string[] files = [Path.Combine(h.Scope.UnitDirectory, "updaemon.timer"), Path.Combine(h.Scope.UnitDirectory, "updaemon.service")];
            foreach (string file in files)
            {
                File.WriteAllText(file, $"0.x {Path.GetFileName(file)}");
            }

            return new ZeroXTimer(files, isOn, interval);
        }

        [Fact]
        public async Task RunAsync_ZeroXSetup_ReplacesTheConfigKeepingItsRetention()
        {
            using (AppHarness h = ZeroXSetup())
            {
                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.Equal(ZeroXLayout.ReleaseRetentionCount, (await h.Config.LoadConfigAsync()).ReleaseRetentionCount);
                Assert.Contains("The updaemon 0.x setup is converted.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_Service_KeepsItsRemoteSettingsAndVersionsWithoutDownloading()
        {
            using (AppHarness h = ZeroXSetup())
            {
                await h.Converter().RunAsync(dryRun: false);

                InstalledApp api = (await h.Config.GetAppAsync("my-api"))!;
                Assert.Equal(("github", "owner/my-api"), (api.Plugin, api.Remote));
                Assert.Equal(new Dictionary<string, string> { ["API_URL"] = "https://api.example" }, api.Settings);
                Assert.Equal(new Version(1, 0, 0), await h.Versions.GetCurrentAsync(new AppName("my-api")));
                Assert.Equal(new[] { new Version(0, 9, 0), new Version(1, 0, 0) }, h.Versions.GetRetained(new AppName("my-api")));
                Assert.Empty(h.Client.Downloads);
                Assert.Contains("Converted my-api (service) at 1.0.0.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_Service_GetsTheNewUnitAndIsRestartedWithIt()
        {
            using (AppHarness h = ZeroXSetup())
            {
                await h.Converter().RunAsync(dryRun: false);

                string unit = File.ReadAllText(ZeroXLayout.UnitPath(h));
                Assert.Contains($"ExecStart=\"{h.Scope.GetServiceWrapperPath("my-api")}\"", unit);
                Assert.Contains("API_URL=\"https://api.example\"", File.ReadAllText(h.Scope.GetSettingsFilePath("my-api")));
                Assert.Equal(ZeroXLayout.Unit(h, ZeroXLayout.Settings), File.ReadAllText(Backup(h, "my-api.service")));
                Assert.Contains(MockServiceManager.Call(nameof(IServiceManager.RestartServiceAsync), "my-api"), h.ServiceManager.MethodCalls);
            }
        }

        /// <summary>As a download is: 0.x left that to who ran it.</summary>
        [Fact]
        public async Task RunAsync_Service_MakesItsCurrentVersionReadyToRun()
        {
            using (AppHarness h = ZeroXSetup())
            {
                const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                string directory = Path.Combine(h.Scope.GetAppDirectory("my-api"), "1.0.0");
                string executable = Path.Combine(directory, "my-api");
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.SetUnixFileMode(directory, ownerOnly);
                h.Permissions.SetsModes = true;

                await h.Converter().RunAsync(dryRun: false);

                Assert.Equal(AtomicFile.Executable, File.GetUnixFileMode(executable));
                Assert.True(File.GetUnixFileMode(directory).HasFlag(UnixFileMode.OtherRead | UnixFileMode.OtherExecute));
            }
        }

        [Fact]
        public async Task RunAsync_ServiceWithRestartManual_IsNotRestartedAndSaysSo()
        {
            using (AppHarness h = ZeroXSetup())
            {
                File.WriteAllText(h.Scope.ConfigFilePath, ZeroXLayout.Config(ZeroXLayout.PluginPath(h), apiRestarts: false));

                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.DoesNotContain(MockServiceManager.Call(nameof(IServiceManager.RestartServiceAsync), "my-api"), h.ServiceManager.MethodCalls);
                Assert.Contains("Converted my-api (service) at 1.0.0; it runs as before until it restarts (restart=manual).", h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_CliTool_BecomesAWrapperInPlaceOfItsLinks()
        {
            using (AppHarness h = ZeroXSetup())
            {
                await h.Converter().RunAsync(dryRun: false);

                Assert.Equal("ripgrep", AppWrapper.ReadOwner(h.Scope.GetCommandPath("rg")));
                Assert.False(File.Exists(h.Scope.GetCommandPath("ripgrep")));
                Assert.Equal("rg", (await h.Deployer.GetCurrentManifestAsync("ripgrep")).Command);
                Assert.Contains("Converted ripgrep (command) at 14.0.0; its command is now only 'rg'.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_KeptOlderVersion_CanBeRolledBackTo()
        {
            using (AppHarness h = ZeroXSetup())
            {
                await h.Converter().RunAsync(dryRun: false);

                Assert.Equal("my-api", (await h.Deployer.OpenRetainedAsync("my-api", new Version(0, 9, 0))).Manifest.Executable);
            }
        }

        [Fact]
        public async Task RunAsync_RegistryPlugin_IsReinstalledFromTheRunningReleaseKeepingItsSecrets()
        {
            using (AppHarness h = ZeroXSetup())
            {
                await h.Converter().RunAsync(dryRun: false);

                InstalledPluginInfo plugin = (await h.Config.GetPluginAsync("github"))!;
                Assert.Equal(new PluginSource.Registry("github", AppHarness.RunningVersion), plugin.Source);
                Assert.Contains(h.PluginDownloader.MethodCalls, call => call.Contains("/download/v1.0.0/Updaemon.GithubDistributionService-linux-arm64"));
                Assert.Equal("abc", await h.Secrets.GetSecretAsync("github", "githubToken"));
            }
        }

        [Fact]
        public async Task RunAsync_PluginNotInTheRegistry_IsReportedWithHowToInstallIt()
        {
            using (AppHarness h = ZeroXSetup())
            {
                File.WriteAllText(h.Scope.ConfigFilePath, ZeroXLayout.Config(Path.Combine(h.Scope.PluginsDirectory, "github", "my-own-plugin")));

                Assert.False(await h.Converter().RunAsync(dryRun: false));

                Assert.Contains("Not converted: the 'github' plugin is not in the plugin registry; install it again with 'updaemon dist-install --as github <url>'.", h.Output.Errors);
                Assert.Null(await h.Config.GetPluginAsync("github"));
            }
        }

        [Fact]
        public async Task RunAsync_PluginReinstallFails_IsReportedAndTheAppsAreStillConverted()
        {
            using (AppHarness h = ZeroXSetup())
            {
                h.PluginDownloader.ExceptionToThrow = new HttpRequestException("offline");

                Assert.False(await h.Converter().RunAsync(dryRun: false));

                Assert.Contains("Not converted: updating the 'github' plugin failed: offline", h.Output.Errors);
                Assert.True(File.Exists(ZeroXLayout.PluginPath(h)));
                Assert.NotNull(await h.Config.GetAppAsync("my-api"));
            }
        }

        [Fact]
        public async Task RunAsync_AgainAfterThePluginReinstallFailed_ReinstallsIt()
        {
            using (AppHarness h = ZeroXSetup())
            {
                h.PluginDownloader.ExceptionToThrow = new HttpRequestException("offline");
                await h.Converter().RunAsync(dryRun: false);
                h.PluginDownloader.ExceptionToThrow = null;

                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.Equal(ZeroXLayout.PluginPath(h), (await h.Config.GetPluginAsync("github"))!.Path);
                Assert.Equal(MockPluginDownloader.PluginBytes, File.ReadAllBytes(ZeroXLayout.PluginPath(h)));
            }
        }

        [Fact]
        public async Task RunAsync_BacksUpTheConfigAndTemplate()
        {
            using (AppHarness h = ZeroXSetup())
            {
                string zeroXConfig = File.ReadAllText(h.Scope.ConfigFilePath);

                await h.Converter().RunAsync(dryRun: false);

                Assert.Equal(zeroXConfig, File.ReadAllText(Backup(h, "config.json")));
                Assert.True(File.Exists(Backup(h, "default-unit.template")));
                Assert.False(File.Exists(Path.Combine(h.Scope.RootDirectory, "default-unit.template")));
            }
        }

        [Theory]
        [InlineData(true, 3600, "  rewrite the update timer, keeping its interval of 01:00:00", "The update timer runs every 01:00:00 as before.")]
        [InlineData(false, 3600, "  leave the update timer off, as it is", "The update timer was off and stays off.")]
        [InlineData(true, null, "  turn the update timer off, as its interval cannot be read", "The update timer is off, as its interval could not be read; set it with 'updaemon timer <interval>'.")]
        public async Task RunAsync_ZeroXTimer_IsReplacedAsItWas(bool isOn, int? seconds, string preview, string done)
        {
            using (AppHarness h = ZeroXSetup())
            {
                ZeroXTimer timer = TimerIn(h, isOn, seconds == null ? null : TimeSpan.FromSeconds(seconds.Value));
                h.Timer.ZeroX = timer;

                await h.Converter().RunAsync(dryRun: true);
                await h.Converter().RunAsync(dryRun: false);

                Assert.Contains(preview, h.Output.Messages);
                Assert.Contains(done, h.Output.Messages);
                Assert.Equal(isOn && seconds != null ? TimeSpan.FromSeconds(seconds.Value) : (TimeSpan?)null, h.Timer.LastSetInterval);
                Assert.Equal("0.x updaemon.timer", File.ReadAllText(Backup(h, "updaemon.timer")));
                Assert.Equal("0.x updaemon.service", File.ReadAllText(Backup(h, "updaemon.service")));
                Assert.Null(h.Timer.ZeroX);

                // A timer left off is recorded so, and install.sh's 'timer 1h --keep' then leaves it off.
                Assert.Equal(h.Timer.LastSetInterval == null, (await h.Config.LoadConfigAsync()).TimerTurnedOff);
                Assert.Equal(0, await h.TimerCommand().ExecuteAsync(new[] { "1h", "--keep" }));
                Assert.Equal(isOn && seconds != null, h.Timer.IsEnabled);
            }
        }

        [Fact]
        public async Task RunAsync_NewTimerCannotBeSet_KeepsTheZeroXOneAndTriesAgainOnTheNextRun()
        {
            using (AppHarness h = ZeroXSetup())
            {
                h.Timer.ZeroX = TimerIn(h, isOn: true, TimeSpan.FromHours(1));
                h.Timer.SetFailure = new InvalidOperationException("launchctl failed.");

                Assert.False(await h.Converter().RunAsync(dryRun: false));

                Assert.Contains("Not converted: the update timer: launchctl failed.", h.Output.Errors);
                Assert.NotNull(h.Timer.ZeroX);

                h.Timer.SetFailure = null;
                Assert.True(await h.Converter().RunAsync(dryRun: false));
                Assert.Equal(TimeSpan.FromHours(1), h.Timer.LastSetInterval);
                Assert.Null(h.Timer.ZeroX);
            }
        }

        [Fact]
        public async Task RunAsync_HandEditedUnit_LeavesThatAppAloneAndSaysWhy()
        {
            using (AppHarness h = ZeroXSetup(serviceLines: "Group=api"))
            {
                Assert.False(await h.Converter().RunAsync(dryRun: false));

                Assert.Null(await h.Config.GetAppAsync("my-api"));
                Assert.Equal(ZeroXLayout.Unit(h, "Group=api"), File.ReadAllText(ZeroXLayout.UnitPath(h)));
                Assert.Contains(
                    $"Not converted: my-api: {ZeroXLayout.UnitPath(h)} was edited by hand: the line 'Group=api' differs from what updaemon 0.x wrote. Make it what updaemon 0.x wrote, apart from Environment= and User= lines.",
                    h.Output.Errors);
                Assert.NotNull(await h.Config.GetAppAsync("ripgrep"));
            }
        }

        [Fact]
        public async Task RunAsync_UnitWithUser_ConvertsItToRunAsThatUser()
        {
            using (AppHarness h = ZeroXSetup(serviceLines: $"{ZeroXLayout.Settings}\nUser=pi"))
            {
                h.Permissions.ExistingUsers.Add("pi");

                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.Equal("pi", (await h.Config.GetAppAsync("my-api"))!.RunAs);
                Assert.Contains("User=pi", File.ReadAllText(ZeroXLayout.UnitPath(h)).Split('\n'));
                Assert.Contains($"SetOwnerAsync:{h.Scope.GetDataDirectory("my-api")}:pi", h.Permissions.MethodCalls);
                Assert.Contains("Converted my-api (service) at 1.0.0, running as pi.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_DropInSetsUser_ConvertsItToRunAsThatUserAndLeavesTheDropInWhereSystemdAppliesIt()
        {
            using (AppHarness h = ZeroXSetup())
            {
                h.Permissions.ExistingUsers.Add("pi");
                string dropInText = "[Service]\nUser=pi\nEnvironment=TOKEN=s3cret\n";
                string dropIn = DropIn(h, "run-as-pi.conf", dropInText);

                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.Equal("pi", (await h.Config.GetAppAsync("my-api"))!.RunAs);
                // The new unit has the 0.x unit's name, so systemd still applies the drop-in to it.
                Assert.Equal(ZeroXDropIn.DirectoryOf(ZeroXLayout.UnitPath(h)), Path.GetDirectoryName(dropIn));
                Assert.Contains($"ExecStart=\"{h.Scope.GetServiceWrapperPath("my-api")}\"", File.ReadAllText(ZeroXLayout.UnitPath(h)));
                Assert.Equal(dropInText, File.ReadAllText(dropIn));
                Assert.Equal(new[] { "run-as-pi.conf" }, Directory.GetFiles(Path.GetDirectoryName(dropIn)!).Select(Path.GetFileName));
                Assert.Contains("Converted my-api (service) at 1.0.0, running as pi.", h.Output.Messages);
                Assert.Contains($"  Its drop-in {dropIn} stays in place and still sets User=pi; the variable TOKEN.", h.Output.Messages);
                Assert.DoesNotContain(h.Output.Messages, message => message.Contains("s3cret"));
            }
        }

        [Theory]
        [InlineData("User=pi", "pi", "pi")]
        [InlineData("User=", null, "root")]
        public async Task RunAsync_DropInOverridesTheUnitsUser_RunsAsTheDropInSaysAndSaysSo(string dropInUser, string? runAs, string described)
        {
            using (AppHarness h = ZeroXSetup(serviceLines: "User=api"))
            {
                h.Permissions.ExistingUsers.UnionWith(new[] { "pi", "api" });
                DropIn(h, "10-other.conf", "[Service]\nUser=api\n");
                string dropIn = DropIn(h, "override.conf", $"[Service]\n{dropInUser}\n");

                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.Equal(runAs, (await h.Config.GetAppAsync("my-api"))!.RunAs);
                Assert.Contains($"  Its drop-in {dropIn} overrides its unit's User=api, so it runs as {described}, as before.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_DropInSetsAVariableTheUnitSetsToo_LeavesItToTheDropIn()
        {
            using (AppHarness h = ZeroXSetup(serviceLines: "Environment=API_URL=https://api.example LOG_LEVEL=debug"))
            {
                DropIn(h, "override.conf", "[Service]\nEnvironment=API_URL=https://override.example\n");

                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.Equal(new Dictionary<string, string> { ["LOG_LEVEL"] = "debug" }, (await h.Config.GetAppAsync("my-api"))!.Settings);
                Assert.DoesNotContain("API_URL", File.ReadAllText(h.Scope.GetSettingsFilePath("my-api")));
            }
        }

        public static TheoryData<Action<AppHarness>, string, Func<AppHarness, string>> Blockers => new TheoryData<Action<AppHarness>, string, Func<AppHarness, string>>
        {
            {
                h => File.WriteAllText(h.Scope.ConfigFilePath, File.ReadAllText(h.Scope.ConfigFilePath).Replace("\"localName\": \"ripgrep\"", "\"localName\": \"updaemon\"")),
                "updaemon",
                h => "'updaemon' is not a valid app name; reinstall it under another name with 'updaemon install'"
            },
            {
                h => File.Delete(h.Scope.GetCurrentLinkPath("my-api")),
                "my-api",
                h => $"it has no current version (0.x 'init' never ran); remove {ZeroXLayout.UnitPath(h)}, then install it with 'updaemon install'"
            },
            {
                h => File.Delete(ZeroXLayout.UnitPath(h)),
                "my-api",
                h => $"its unit {ZeroXLayout.UnitPath(h)} is missing"
            },
            {
                h => File.Delete(Path.Combine(h.Scope.GetAppDirectory("my-api"), "1.0.0", "my-api")),
                "my-api",
                h => $"its executable {Path.Combine(h.Scope.GetAppDirectory("my-api"), "1.0.0", "my-api")} is missing"
            },
            {
                h => File.Delete(Path.Combine(h.Scope.GetAppDirectory("ripgrep"), "14.0.0", "rg")),
                "ripgrep",
                h => $"no executable named 'rg' is in {Path.Combine(h.Scope.GetAppDirectory("ripgrep"), "14.0.0")}"
            },
            {
                h => { File.Delete(h.Scope.GetCommandPath("rg")); File.WriteAllText(h.Scope.GetCommandPath("rg"), "someone else's"); },
                "ripgrep",
                h => $"{h.Scope.GetCommandPath("rg")} is not the link updaemon 0.x made"
            },
            {
                h => File.WriteAllText(ZeroXLayout.UnitPath(h), ZeroXLayout.Unit(h, "User=ghost")),
                "my-api",
                h => "it runs as 'ghost', and there is no user by that name"
            },
            {
                h => DropIn(h, "override.conf", "[Service]\nUser=ghost\n"),
                "my-api",
                h => "it runs as 'ghost', and there is no user by that name"
            },
            {
                h => DropIn(h, "override.conf", "[Service]\nEnvironment=\n"),
                "my-api",
                h => $"its drop-in {Path.Combine(h.Scope.UnitDirectory, "my-api.service.d", "override.conf")} has an Environment= line updaemon cannot read"
            },
        };

        [Theory]
        [MemberData(nameof(Blockers))]
        public async Task RunAsync_AppThatCannotBeConverted_IsLeftOutWithTheReasonInThePreviewAndTheRun(Action<AppHarness> block, string app, Func<AppHarness, string> problem)
        {
            using (AppHarness h = ZeroXSetup())
            {
                block(h);

                await h.Converter().RunAsync(dryRun: true);
                Assert.False(await h.Converter().RunAsync(dryRun: false));

                Assert.Contains($"  leave out {app}: {problem(h)}", h.Output.Messages);
                Assert.Contains($"Not converted: {app}: {problem(h)}.", h.Output.Errors);
                Assert.Null(await h.Config.GetAppAsync(app));
            }
        }

        [Fact]
        public async Task RunAsync_AgainAfterTheHandEditIsUndone_ConvertsWhatWasLeft()
        {
            using (AppHarness h = ZeroXSetup(serviceLines: "Group=api"))
            {
                await h.Converter().RunAsync(dryRun: false);
                File.WriteAllText(ZeroXLayout.UnitPath(h), ZeroXLayout.Unit(h));
                h.Output.Clear();
                h.PluginDownloader.MethodCalls.Clear();

                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.NotNull(await h.Config.GetAppAsync("my-api"));
                Assert.Contains("Converted my-api (service) at 1.0.0.", h.Output.Messages);
                Assert.DoesNotContain(h.Output.Messages, message => message.Contains("ripgrep"));
                Assert.Empty(h.PluginDownloader.MethodCalls);
            }
        }

        [Fact]
        public async Task RunAsync_NothingLeft_SaysSo()
        {
            using (AppHarness h = ZeroXSetup())
            {
                await h.Converter().RunAsync(dryRun: false);
                h.Output.Clear();

                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.Equal(new[] { "Everything from updaemon 0.x is converted; there is nothing left to do." }, h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_ServiceDoesNotStayRunningWithTheNewUnit_PutsTheOldOneBack()
        {
            using (AppHarness h = ZeroXSetup())
            {
                h.ServiceManager.CrashOnNextStart.Add("my-api");

                Assert.False(await h.Converter().RunAsync(dryRun: false));

                Assert.Null(await h.Config.GetAppAsync("my-api"));
                Assert.Equal(ZeroXLayout.Unit(h, ZeroXLayout.Settings), File.ReadAllText(ZeroXLayout.UnitPath(h)));
                Assert.False(File.Exists(h.Scope.GetSettingsFilePath("my-api")));
                Assert.Contains(
                    "Not converted: my-api: 1.0.0 did not stay running with its new unit, so its 0.x unit is back. See 'updaemon logs my-api'.",
                    h.Output.Errors);
            }
        }

        [Fact]
        public async Task RunAsync_CliToolFails_PutsItsLinksBack()
        {
            using (AppHarness h = ZeroXSetup())
            {
                Directory.CreateDirectory(h.Scope.GetSettingsFilePath("ripgrep"));
                string target = Path.Combine(h.Scope.GetCurrentLinkPath("ripgrep"), "rg");

                Assert.False(await h.Converter().RunAsync(dryRun: false));

                Assert.Null(await h.Config.GetAppAsync("ripgrep"));
                Assert.Equal(target, new FileInfo(h.Scope.GetCommandPath("rg")).LinkTarget);
                Assert.Equal(target, new FileInfo(h.Scope.GetCommandPath("ripgrep")).LinkTarget);
                Assert.Contains(h.Output.Errors, error => error.StartsWith("Not converted: ripgrep: "));
            }
        }

        [Fact]
        public async Task RunAsync_CancelledWhileAServiceStarts_PutsItsOldUnitBack()
        {
            using (AppHarness h = ZeroXSetup())
            {
                h.ServiceManager.RestartFailure = new OperationCanceledException();

                await Assert.ThrowsAsync<OperationCanceledException>(() => h.Converter().RunAsync(dryRun: false));

                Assert.Null(await h.Config.GetAppAsync("my-api"));
                Assert.Equal(ZeroXLayout.Unit(h, ZeroXLayout.Settings), File.ReadAllText(ZeroXLayout.UnitPath(h)));
                Assert.False(File.Exists(h.Scope.GetSettingsFilePath("my-api")));
            }
        }

        [Fact]
        public async Task RunAsync_AgainAfterAConversionWasCutOff_ConvertsTheServiceFromItsKeptUnit()
        {
            using (AppHarness h = ZeroXSetup())
            {
                h.ServiceManager.CrashOnNextStart.Add("my-api");
                await h.Converter().RunAsync(dryRun: false);
                // What a conversion killed after writing the new files leaves: the settings file and the new unit.
                File.WriteAllText(h.Scope.GetSettingsFilePath("my-api"), "API_URL=\"https://api.example\"\n");
                File.WriteAllText(ZeroXLayout.UnitPath(h), "[Service]\nExecStart=\"/new/unit\"\n");

                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.Equal("https://api.example", (await h.Config.GetAppAsync("my-api"))!.Settings["API_URL"]);
                Assert.Contains($"ExecStart=\"{h.Scope.GetServiceWrapperPath("my-api")}\"", File.ReadAllText(ZeroXLayout.UnitPath(h)));
            }
        }

        [Fact]
        public async Task RunAsync_AgainAfterAConversionWasCutOff_ConvertsTheCliToolInPlaceOfItsWrapper()
        {
            using (AppHarness h = ZeroXSetup())
            {
                File.Delete(h.Scope.GetCommandPath("rg"));
                File.WriteAllText(h.Scope.GetCommandPath("rg"), "someone else's");
                await h.Converter().RunAsync(dryRun: false);
                // What a conversion killed after writing the wrapper leaves: the wrapper, and no alias link.
                File.Delete(h.Scope.GetCommandPath("ripgrep"));
                File.WriteAllText(h.Scope.GetCommandPath("rg"), AppWrapper.ForCommand("ripgrep", "/x/rg", "/x/settings", "/x/secrets", new Dictionary<string, string>()));

                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.Equal("rg", (await h.Config.GetAppAsync("ripgrep"))!.Manifest!.Command);
                Assert.Equal("ripgrep", AppWrapper.ReadOwner(h.Scope.GetCommandPath("rg")));
            }
        }

        [Fact]
        public async Task RunAsync_Again_KeepsTheFirstBackupOfAUnit()
        {
            using (AppHarness h = ZeroXSetup())
            {
                h.ServiceManager.CrashOnNextStart.Add("my-api");
                await h.Converter().RunAsync(dryRun: false);
                File.WriteAllText(ZeroXLayout.UnitPath(h), ZeroXLayout.Unit(h, "Environment=API_URL=https://other.example"));

                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.Equal(ZeroXLayout.Unit(h, ZeroXLayout.Settings), File.ReadAllText(Backup(h, "my-api.service")));
                Assert.Equal("https://other.example", (await h.Config.GetAppAsync("my-api"))!.Settings["API_URL"]);
            }
        }

        [Fact]
        public async Task RunAsync_AnotherZeroXConfigWhileABackupIsKept_IsRefusedAndChangesNothing()
        {
            using (AppHarness h = ZeroXSetup())
            {
                await h.Converter().RunAsync(dryRun: false);
                string backup = File.ReadAllText(Backup(h, "config.json"));
                string otherZeroXConfig = ZeroXLayout.Config("/elsewhere/plugin", apiRestarts: false);
                File.WriteAllText(h.Scope.ConfigFilePath, otherZeroXConfig);
                h.Output.Clear();

                Assert.False(await h.Converter().RunAsync(dryRun: false));

                Assert.Equal(backup, File.ReadAllText(Backup(h, "config.json")));
                Assert.Equal(otherZeroXConfig, File.ReadAllText(h.Scope.ConfigFilePath));
                Assert.Equal(
                    new[] { $"Error: {Backup(h, "config.json")} holds the 0.x config of an earlier conversion, which 'updaemon migrate' still converts from. Move {h.Scope.ZeroXBackupDirectory} aside to convert this 0.x config instead." },
                    h.Output.Errors);
            }
        }

        [Fact]
        public async Task RunAsync_DryRun_SaysWhatItWouldDoAndChangesNothing()
        {
            using (AppHarness h = ZeroXSetup(serviceLines: "Environment=API_URL=https://api.example \"LOG_LEVEL=debug\""))
            {
                h.Timer.ZeroX = HourlyTimer;
                string zeroXConfig = File.ReadAllText(h.Scope.ConfigFilePath);

                Assert.True(await h.Converter().RunAsync(dryRun: true));

                Assert.Equal(
                    new[]
                    {
                        "Converting the updaemon 0.x setup would:",
                        "  reinstall the 'github' plugin from this updaemon's release, keeping its secrets",
                        "  convert my-api (service) at 1.0.0, with the settings API_URL, LOG_LEVEL",
                        "  convert ripgrep (command) at 14.0.0",
                        "  rewrite the update timer, keeping its interval of 01:00:00",
                        "Nothing was changed.",
                    },
                    h.Output.Messages);
                Assert.Equal(zeroXConfig, File.ReadAllText(h.Scope.ConfigFilePath));
                Assert.False(Directory.Exists(h.Scope.ZeroXBackupDirectory));
                Assert.Empty(h.ServiceManager.MethodCalls);
                Assert.Empty(h.PluginDownloader.MethodCalls);
                Assert.Empty(h.Timer.MethodCalls);
            }
        }

        [Fact]
        public async Task RunAsync_DryRunWithDropIns_NamesEachAndWhatItStillSets()
        {
            using (AppHarness h = ZeroXSetup(serviceLines: $"{ZeroXLayout.Settings}\nUser=api"))
            {
                h.Permissions.ExistingUsers.Add("pi");
                string runAs = DropIn(h, "run-as-pi.conf", "[Service]\nUser=pi\n");
                string secrets = DropIn(h, "secrets.conf", "[Unit]\nAfter=postgresql.service\n[Service]\nEnvironment=\"DB_PASSWORD=p a s s\"\n");

                Assert.True(await h.Converter().RunAsync(dryRun: true));

                Assert.Equal(
                    new[]
                    {
                        "Converting the updaemon 0.x setup would:",
                        "  reinstall the 'github' plugin from this updaemon's release, keeping its secrets",
                        "  convert my-api (service) at 1.0.0, running as pi, with the settings API_URL",
                        $"    its drop-in {runAs} stays in place and still sets User=pi",
                        $"    its drop-in {secrets} stays in place and still sets After; the variable DB_PASSWORD",
                        $"    its drop-in {runAs} overrides its unit's User=api, so it runs as pi, as before",
                        "  convert ripgrep (command) at 14.0.0",
                        "Nothing was changed.",
                    },
                    h.Output.Messages);
            }
        }

        [Fact]
        public async Task RunAsync_DryRunWithAPluginNotInTheRegistry_SaysItIsLeftOut()
        {
            using (AppHarness h = ZeroXSetup())
            {
                File.WriteAllText(h.Scope.ConfigFilePath, ZeroXLayout.Config(Path.Combine(h.Scope.PluginsDirectory, "github", "my-own-plugin")));

                await h.Converter().RunAsync(dryRun: true);

                Assert.Contains("  leave out the 'github' plugin, which is not in the plugin registry: install it again with 'updaemon dist-install --as github <url>'", h.Output.Messages);
            }
        }

        [Fact]
        public async Task IsPending_SystemScopeConfigWithoutFormat_UntilConverted()
        {
            using (AppHarness h = ZeroXSetup())
            {
                Assert.True(h.Converter().IsPending());

                await h.Converter().RunAsync(dryRun: false);

                Assert.False(h.Converter().IsPending());
            }
        }

        [Fact]
        public void IsPending_UserScope_IsNever()
        {
            using (AppHarness h = new AppHarness(isSystem: false))
            {
                ZeroXLayout.Write(h);

                Assert.False(h.Converter().IsPending());
            }
        }

        [Theory]
        [InlineData("{\"format\":1,\"apps\":[]}")]
        [InlineData("{\"format\":null}")]
        [InlineData("[]")]
        [InlineData("not json")]
        public void IsPending_ConfigWithAFormatOrNoObject_IsNot(string json)
        {
            using (AppHarness h = new AppHarness(isSystem: true))
            {
                Directory.CreateDirectory(h.Scope.RootDirectory);
                File.WriteAllText(h.Scope.ConfigFilePath, json);

                Assert.False(h.Converter().IsPending());
            }
        }

        [Fact]
        public async Task RunAsync_NoZeroXSetup_SaysSo()
        {
            using (AppHarness h = new AppHarness(isSystem: true))
            {
                Assert.True(await h.Converter().RunAsync(dryRun: false));

                Assert.Equal(new[] { "There is no updaemon 0.x setup to convert." }, h.Output.Messages);
            }
        }
    }
}
