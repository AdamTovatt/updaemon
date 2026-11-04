using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class InstallCommandTests
    {
        [Fact]
        public async Task Install_FromManifest_SetsUpTheCommandWithSettingsAndSecretsAndStartsTheService()
        {
            using (AppHarness h = new AppHarness())
            {
                int exitCode = await h.InstallByteShelfAsync();

                Assert.Equal(0, exitCode);
                InstalledApp app = (await h.Config.GetAppAsync("byteshelf"))!;
                Assert.Null(app.Manifest);
                Assert.Equal(new Dictionary<string, string> { ["BYTESHELF_URL"] = "https://shelf" }, app.Settings);
                Assert.Equal("key1", (await h.Secrets.GetAppSecretsAsync("byteshelf"))["BYTESHELF_API_KEY"]);

                string output = await Shell.RunAsync(h.Scope.GetCommandPath("byteshelf"), "status");
                string dataDirectory = h.Scope.GetDataDirectory("byteshelf");
                Assert.Equal($"BYTESHELF_URL=https://shelf\nBYTESHELF_API_KEY=key1\nBYTESHELF_LOG_LEVEL=info\nUPDAEMON_DATA_DIR={dataDirectory}\narg=status\n", output);
                Assert.True(Directory.Exists(dataDirectory));

                Assert.Contains(MockServiceManager.Call(nameof(IServiceManager.EnableServiceAsync), "byteshelf"), h.ServiceManager.MethodCalls);
                Assert.Contains(MockServiceManager.Call(nameof(IServiceManager.StartServiceAsync), "byteshelf"), h.ServiceManager.MethodCalls);
                Assert.Contains("Installed byteshelf 1.0.0 (command + service).", h.Output.Messages);
            }
        }

        [Fact]
        public async Task Install_Service_ItsUnitRunsTheWrapperWhichGetsTheSettingsAndSecrets()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                string unit = await File.ReadAllTextAsync(h.Scope.GetUnitFilePath("byteshelf"));
                string serviceWrapper = h.Scope.GetServiceWrapperPath("byteshelf");
                Assert.Contains($"ExecStart=\"{serviceWrapper}\" \"sync\" \"run\"", unit);
                Assert.DoesNotContain("key1", unit);
                string dataDirectory = h.Scope.GetDataDirectory("byteshelf");
                Assert.Equal(
                    $"BYTESHELF_URL=https://shelf\nBYTESHELF_API_KEY=key1\nBYTESHELF_LOG_LEVEL=info\nUPDAEMON_DATA_DIR={dataDirectory}\narg=sync\narg=run\n",
                    await Shell.RunAsync(serviceWrapper, "sync", "run"));
            }
        }

        [Fact]
        public async Task Install_WithoutFrom_ShowsAnExampleAndThePlugins()
        {
            using (AppHarness h = new AppHarness())
            {
                int exitCode = await h.Install().ExecuteAsync(new[] { "tool" });

                Assert.Equal(1, exitCode);
                Assert.Equal(
                    new[]
                    {
                        "Error: Say where to install tool from, for example: updaemon install tool --from github:<owner>/<repo>",
                        "Plugins: github (installed on first use).",
                    },
                    h.Output.Errors);
            }
        }

        [Fact]
        public async Task Install_WithoutFrom_ListsTheInstalledPluginsFirst()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync("store");
                await h.AddPluginAsync("github");

                await h.Install().ExecuteAsync(new[] { "tool" });

                Assert.Equal("Plugins: github, store.", h.Output.Errors[^1]);
            }
        }

        [Fact]
        public async Task Install_RegistryPluginMissing_InstallsItFromTheRunningRelease()
        {
            using (AppHarness h = new AppHarness())
            {
                h.AddRelease("owner/tool", "1.0.0", """{ "executable": "tool", "command": "tool" }""", executable: "tool");

                int exitCode = await h.Install().ExecuteAsync(new[] { "tool", "--from", "github:owner/tool" });

                Assert.Equal(0, exitCode);
                Assert.Equal(new PluginSource.Registry("github", AppHarness.RunningVersion), (await h.Config.GetPluginAsync("github"))!.Source);
                Assert.Contains(h.PluginDownloader.MethodCalls, call => call.Contains("/releases/download/v1.0.0/Updaemon.GithubDistributionService-linux-arm64"));
                Assert.True(File.Exists(h.Scope.GetCommandPath("tool")));
            }
        }

        [Fact]
        public async Task Install_PluginRefusesTheRemote_FailsInOneLineNamingTheApp()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.Client.RefusedRemotes["owner/nope"] = "The repository 'owner/nope' was not found. Check the name after 'github:'.";

                int exitCode = await h.Install().ExecuteAsync(new[] { "nope", "--from", "github:owner/nope" });

                Assert.Equal(1, exitCode);
                Assert.Equal(new[] { "Error: nope: The repository 'owner/nope' was not found. Check the name after 'github:'." }, h.Output.Errors);
                Assert.Empty(h.Client.Downloads);
            }
        }

        [Fact]
        public async Task Install_ServiceDoesNotStayRunning_StaysInstalledAndPointsToTheLogs()
        {
            using (AppHarness h = new AppHarness())
            {
                h.ServiceManager.CrashOnNextStart.Add("byteshelf");

                int exitCode = await h.InstallByteShelfAsync();

                Assert.Equal(1, exitCode);
                Assert.Contains("Error: byteshelf: 1.0.0 did not stay running. See 'updaemon logs byteshelf'.", h.Output.Errors);
                Assert.NotNull(await h.Config.GetAppAsync("byteshelf"));
                Assert.True(File.Exists(h.Scope.GetUnitFilePath("byteshelf")));
            }
        }

        [Theory]
        [InlineData(false, "Warning: 'byteshelf' is also installed for the whole machine. This installs a separate copy for user ada.")]
        [InlineData(true, "Warning: 'byteshelf' is also installed for user ada. This installs a separate copy for the whole machine.")]
        public async Task Install_SameNameInTheOtherScope_WarnsAndInstalls(bool isSystem, string warning)
        {
            using (AppHarness h = new AppHarness(isSystem))
            {
                await new ConfigManager(h.OtherScope).SaveAppAsync(new InstalledApp { Name = "byteshelf", Plugin = "github", Remote = "owner/byteshelf" });

                int exitCode = await h.InstallByteShelfAsync();

                Assert.Equal(0, exitCode);
                Assert.Equal(new[] { warning }, h.Output.Errors);
                Assert.NotNull(await h.Config.GetAppAsync("byteshelf"));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Install_OtherScopeWithOtherAppsOrAnUnreadableConfig_DoesNotWarn(bool unreadable)
        {
            using (AppHarness h = new AppHarness())
            {
                await new ConfigManager(h.OtherScope).SaveAppAsync(new InstalledApp { Name = "other", Plugin = "github", Remote = "owner/other" });
                if (unreadable)
                {
                    await File.WriteAllTextAsync(h.OtherScope.ConfigFilePath, "not json");
                }

                Assert.Equal(0, await h.InstallByteShelfAsync());

                Assert.Empty(h.Output.Errors);
            }
        }

        private static async Task<AppHarness> InstallWhileServicesStopAtLogoutAsync(string manifest)
        {
            AppHarness h = new AppHarness();
            await h.AddPluginAsync();
            h.AddRelease("owner/app", "1.0.0", manifest, executable: "app");
            h.ServiceManager.ServicesStopAtLogout = true;
            Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "app", "--from", "github:owner/app" }));
            return h;
        }

        [Fact]
        public async Task Install_ServiceWhileServicesStopAtLogout_WarnsNamingTheCommand()
        {
            using (AppHarness h = await InstallWhileServicesStopAtLogoutAsync("""{ "executable": "app", "service": {} }"""))
            {
                string user = Environment.UserName;
                Assert.Equal(
                    new[] { $"Warning: lingering is off for {user}, so your services stop when you log out. To keep them running, run: sudo loginctl enable-linger {user}" },
                    h.Output.Errors);
            }
        }

        [Fact]
        public async Task Install_CommandWhileServicesStopAtLogout_DoesNotWarn()
        {
            using (AppHarness h = await InstallWhileServicesStopAtLogoutAsync("""{ "executable": "app", "command": "app" }"""))
            {
                Assert.Empty(h.Output.Errors);
            }
        }

        [Fact]
        public async Task Install_MissingRequiredSettings_AsksForEach_SecretsWithoutEcho()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/byteshelf", "1.0.0", AppHarness.ByteShelfManifest);
                h.Prompter.Answers.Enqueue("typed-key");
                h.Prompter.Answers.Enqueue("https://typed");

                int exitCode = await h.Install().ExecuteAsync(new[] { "byteshelf", "--from", "github:owner/byteshelf" });

                Assert.Equal(0, exitCode);
                Assert.Equal(new[] { ("BYTESHELF_API_KEY (API key): ", true), ("BYTESHELF_URL (Server URL): ", false) }, h.Prompter.Asked);
                Assert.Equal("https://typed", (await h.Config.GetAppAsync("byteshelf"))!.Settings["BYTESHELF_URL"]);
                Assert.Equal("typed-key", (await h.Secrets.GetAppSecretsAsync("byteshelf"))["BYTESHELF_API_KEY"]);
            }
        }

        [Fact]
        public async Task Install_EmptyAnswer_AsksAgain()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/byteshelf", "1.0.0", AppHarness.ByteShelfManifest);
                h.Prompter.Answers.Enqueue("");
                h.Prompter.Answers.Enqueue("typed-key");

                int exitCode = await h.Install().ExecuteAsync(new[] { "byteshelf", "--from", "github:owner/byteshelf", "--set", "BYTESHELF_URL=u" });

                Assert.Equal(0, exitCode);
                Assert.Equal(2, h.Prompter.Asked.Count);
                Assert.Contains("BYTESHELF_API_KEY is required.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task Install_MissingRequiredSettingWithoutTerminal_FailsNamingIt_AndLeavesNothing()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/byteshelf", "1.0.0", AppHarness.ByteShelfManifest);
                h.Prompter.IsInteractive = false;

                int exitCode = await h.Install().ExecuteAsync(new[] { "byteshelf", "--from", "github:owner/byteshelf", "--set", "BYTESHELF_URL=u" });

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains("BYTESHELF_API_KEY") && e.Contains("--set"));
                Assert.Null(await h.Config.GetAppAsync("byteshelf"));
                Assert.False(Directory.Exists(h.Scope.GetAppDirectory("byteshelf")));
                Assert.False(File.Exists(h.Scope.GetUnitFilePath("byteshelf")));
            }
        }

        [Fact]
        public async Task Install_CancelledAtThePrompt_LeavesNothing()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/byteshelf", "1.0.0", AppHarness.ByteShelfManifest);
                h.Prompter.CancelWhenAsked = true;

                await Assert.ThrowsAsync<OperationCanceledException>(() => h.Install().ExecuteAsync(new[] { "byteshelf", "--from", "github:owner/byteshelf" }));

                Assert.Null(await h.Config.GetAppAsync("byteshelf"));
                Assert.False(Directory.Exists(h.Scope.GetAppDirectory("byteshelf")));
            }
        }

        [Fact]
        public async Task Install_CancelledWhileStarting_RemovesWhatItCreated()
        {
            using (AppHarness h = new AppHarness())
            {
                h.ServiceManager.StartFailure = new OperationCanceledException();

                await Assert.ThrowsAsync<OperationCanceledException>(() => h.InstallByteShelfAsync());

                Assert.Null(await h.Config.GetAppAsync("byteshelf"));
                Assert.False(File.Exists(h.Scope.GetUnitFilePath("byteshelf")));
                Assert.False(Directory.Exists(h.Scope.GetAppDirectory("byteshelf")));
            }
        }

        [Fact]
        public async Task Install_ServiceFailsToStart_RemovesWhatItCreated()
        {
            using (AppHarness h = new AppHarness())
            {
                h.ServiceManager.StartFailure = new InvalidOperationException("Simulated start failure");

                int exitCode = await h.InstallByteShelfAsync();

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains("Simulated start failure"));
                Assert.Null(await h.Config.GetAppAsync("byteshelf"));
                Assert.False(File.Exists(h.Scope.GetCommandPath("byteshelf")));
                Assert.False(File.Exists(h.Scope.GetUnitFilePath("byteshelf")));
                Assert.False(File.Exists(h.Scope.GetSecretsFilePath("byteshelf")));
                Assert.False(Directory.Exists(h.Scope.GetAppDirectory("byteshelf")));
                Assert.False(Directory.Exists(h.Scope.GetDataDirectory("byteshelf")));
            }
        }

        [Fact]
        public async Task Install_ReleaseWithoutManifestOrFlags_FailsNamingTheFlags()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/tool", "2.0.0", manifest: null, executable: "tool-linux/tool");

                int exitCode = await h.Install().ExecuteAsync(new[] { "tool", "--from", "github:owner/tool" });

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains("has no updaemon.json") && e.Contains("--command"));
                Assert.Null(await h.Config.GetAppAsync("tool"));
            }
        }

        [Fact]
        public async Task Install_ReleaseWithoutManifest_StoresTheFlagsAsTheManifest()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/tool", "2.0.0", manifest: null, executable: "tool-linux/tool");

                int exitCode = await h.Install().ExecuteAsync(new[] { "tool", "--from", "github:owner/tool", "--command", "tool" });

                Assert.Equal(0, exitCode);
                AppManifest stored = (await h.Config.GetAppAsync("tool"))!.Manifest!;
                Assert.Equal("tool", stored.Command);
                Assert.Null(stored.Service);
                AppManifest written = (await AppManifest.ReadAsync(h.Scope.GetCurrentLinkPath("tool")))!;
                Assert.Equal("tool-linux/tool", written.Executable);
                Assert.True(File.Exists(h.Scope.GetCommandPath("tool")));
                Assert.False(File.Exists(h.Scope.GetUnitFilePath("tool")));
            }
        }

        [Fact]
        public async Task Install_FlagsForAReleaseWithAManifest_AreIgnoredWithANote()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/byteshelf", "1.0.0", AppHarness.ByteShelfManifest);

                int exitCode = await h.Install().ExecuteAsync(new[]
                {
                    "byteshelf", "--from", "github:owner/byteshelf", "--service",
                    "--set", "BYTESHELF_URL=u", "--set", "BYTESHELF_API_KEY=k",
                });

                Assert.Equal(0, exitCode);
                Assert.Contains("The release has its own updaemon.json; --command and --service are ignored.", h.Output.Messages);
                Assert.Null((await h.Config.GetAppAsync("byteshelf"))!.Manifest);
            }
        }

        [Fact]
        public async Task Install_NoReleasedVersion_Fails()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();

                int exitCode = await h.Install().ExecuteAsync(new[] { "app", "--from", "github:owner/app" });

                Assert.Equal(1, exitCode);
                Assert.Contains("Error: github:owner/app has no released version.", h.Output.Errors);
                Assert.Null(await h.Config.GetAppAsync("app"));
            }
        }

        [Fact]
        public async Task Install_RunAsInTheSystemScope_RunsTheServiceAsThatUser()
        {
            using (AppHarness h = new AppHarness(isSystem: true))
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/api", "1.0.0", """{ "executable": "bin/byteshelf", "service": {}, "settings": { "API_KEY": { "secret": true } } }""");
                h.Permissions.ExistingUsers.Add("api");

                int exitCode = await h.Install().ExecuteAsync(new[] { "api", "--from", "github:owner/api", "--run-as", "api", "--set", "API_KEY=k" });

                Assert.Equal(0, exitCode);
                Assert.Contains("User=api\n", await File.ReadAllTextAsync(h.Scope.GetUnitFilePath("api")));
                Assert.Contains($"SetOwnerAsync:{h.Scope.GetDataDirectory("api")}:api", h.Permissions.MethodCalls);
                // The service's wrapper reads its secrets as that user: the file is theirs from the moment it appears.
                Assert.Contains(h.Permissions.MethodCalls, call => call.StartsWith($"SetOwnerAsync:{h.Scope.GetSecretsFilePath("api")}.") && call.EndsWith(".tmp:api"));
            }
        }

        [Fact]
        public async Task Install_RunAsInTheUserScope_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.Permissions.ExistingUsers.Add("api");

                int exitCode = await h.Install().ExecuteAsync(new[] { "api", "--from", "github:owner/api", "--run-as", "api" });

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains("--run-as"));
                Assert.Empty(h.Client.MethodCalls);
            }
        }

        [Fact]
        public async Task Install_ServiceOfThatNameExistsOutsideUpdaemon_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.ServiceManager.ExistingServices.Add("ssh");

                int exitCode = await h.Install().ExecuteAsync(new[] { "ssh", "--from", "github:owner/ssh" });

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains("A service named 'ssh' already exists") && e.Contains("updaemon 0.x"));
                Assert.Empty(h.Client.MethodCalls);
            }
        }

        [Theory]
        [InlineData("<plugin>:<remote>", "app", "--from", "github")]
        [InlineData("<plugin>:<remote>", "app", "--from", "github:")]
        [InlineData("app: the plugin 'gitlab' is not installed and is not in the plugin registry (github). Install it with 'updaemon dist-install --as gitlab <url>'.", "app", "--from", "gitlab:owner/repo")]
        [InlineData("not a valid environment variable", "app", "--from", "github:owner/app", "--set", "NOT VALID=1")]
        [InlineData("'updaemon' is reserved", "updaemon", "--from", "github:owner/app")]
        [InlineData("not a valid app name", "-app", "--from", "github:owner/app")]
        [InlineData("not a valid command name", "app", "--from", "github:owner/app", "--command", "a/b")]
        [InlineData("Unknown option '--bogus'", "app", "--from", "github:owner/app", "--bogus", "x")]
        [InlineData("Say where to install app from", "app")]
        [InlineData("Usage: updaemon install", "app", "other", "--from", "github:owner/app")]
        public async Task Install_InvalidArguments_FailBeforeDownloading(string expectedError, params string[] args)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();

                int exitCode = await h.Install().ExecuteAsync(args);

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains(expectedError));
                Assert.Empty(h.Client.MethodCalls);
            }
        }

        [Fact]
        public async Task Install_AlreadyInstalled_PointsToUpdate()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                int exitCode = await h.Install().ExecuteAsync(new[] { "byteshelf", "--from", "github:owner/byteshelf" });

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains("updaemon update byteshelf"));
            }
        }
    }
}
