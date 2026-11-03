using Updaemon.Commands;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    /// <summary>
    /// The commands that change an installed app's settings: set, unset and set-args.
    /// </summary>
    public class SettingsCommandsTests
    {
        private static readonly string RestartCall = MockServiceManager.Call(nameof(IServiceManager.RestartServiceAsync), "byteshelf");

        [Fact]
        public async Task Set_DeclaredSettings_GoWhereTheManifestSays_AndReachTheRunningService()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.ServiceManager.ServiceRunningStates["byteshelf"] = true;

                int exitCode = await h.Set().ExecuteAsync(new[] { "byteshelf", "BYTESHELF_API_KEY=key2", "BYTESHELF_LOG_LEVEL=debug" });

                Assert.Equal(0, exitCode);
                Assert.Equal("key2", (await h.Secrets.GetAppSecretsAsync("byteshelf"))["BYTESHELF_API_KEY"]);
                Assert.Equal("debug", (await h.Config.GetAppAsync("byteshelf"))!.Settings["BYTESHELF_LOG_LEVEL"]);
                string output = await Shell.RunAsync(h.Scope.GetCommandPath("byteshelf"));
                Assert.Contains("BYTESHELF_API_KEY=key2\n", output);
                Assert.Contains("BYTESHELF_LOG_LEVEL=debug\n", output);
                Assert.Contains(RestartCall, h.ServiceManager.MethodCalls);
            }
        }

        [Fact]
        public async Task Set_UndeclaredKeyWithSecretFlag_IsStoredAsSecret()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                await h.Set().ExecuteAsync(new[] { "byteshelf", "EXTRA_TOKEN=t", "--secret" });

                Assert.Equal("t", (await h.Secrets.GetAppSecretsAsync("byteshelf"))["EXTRA_TOKEN"]);
                Assert.False((await h.Config.GetAppAsync("byteshelf"))!.Settings.ContainsKey("EXTRA_TOKEN"));
            }
        }

        [Fact]
        public async Task Set_UndeclaredSecretChangedWithoutTheFlag_StaysSecret()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                await h.Set().ExecuteAsync(new[] { "byteshelf", "EXTRA_TOKEN=t", "--secret" });

                await h.Set().ExecuteAsync(new[] { "byteshelf", "EXTRA_TOKEN=t2" });

                Assert.Equal("t2", (await h.Secrets.GetAppSecretsAsync("byteshelf"))["EXTRA_TOKEN"]);
                Assert.False((await h.Config.GetAppAsync("byteshelf"))!.Settings.ContainsKey("EXTRA_TOKEN"));
                Assert.DoesNotContain("t2", await File.ReadAllTextAsync(h.Scope.GetSettingsFilePath("byteshelf")));
            }
        }

        [Fact]
        public async Task Unset_OptionalSetting_FallsBackToTheDefault()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                await h.Set().ExecuteAsync(new[] { "byteshelf", "BYTESHELF_LOG_LEVEL=debug" });

                int exitCode = await h.Unset().ExecuteAsync(new[] { "byteshelf", "BYTESHELF_LOG_LEVEL" });

                Assert.Equal(0, exitCode);
                Dictionary<string, string> settingsFile = await EnvFile.ReadAsync(h.Scope.GetSettingsFilePath("byteshelf"));
                Assert.Equal("info", settingsFile["BYTESHELF_LOG_LEVEL"]);
            }
        }

        [Fact]
        public async Task Unset_RequiredSetting_IsRefusedAndKeepsItsValue()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                int exitCode = await h.Unset().ExecuteAsync(new[] { "byteshelf", "BYTESHELF_API_KEY" });

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains("updaemon set byteshelf BYTESHELF_API_KEY=<value>"));
                Assert.Equal("key1", (await h.Secrets.GetAppSecretsAsync("byteshelf"))["BYTESHELF_API_KEY"]);
            }
        }

        [Fact]
        public async Task Unset_KeyThatWasNotSet_ChangesNothing()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.ServiceManager.ServiceRunningStates["byteshelf"] = true;
                h.ServiceManager.MethodCalls.Clear();

                int exitCode = await h.Unset().ExecuteAsync(new[] { "byteshelf", "NEVER_SET" });

                Assert.Equal(0, exitCode);
                Assert.Contains("NEVER_SET was not set.", h.Output.Messages);
                Assert.DoesNotContain(RestartCall, h.ServiceManager.MethodCalls);
            }
        }

        [Fact]
        public async Task SetArgs_Override_ReplacesTheManifestArguments()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                await h.SetArgs().ExecuteAsync(new[] { "byteshelf", "--", "serve", "--port", "80" });

                Assert.Contains("/bin/byteshelf\" \"serve\" \"--port\" \"80\"\n", await File.ReadAllTextAsync(h.Scope.GetUnitFilePath("byteshelf")));
            }
        }

        [Fact]
        public async Task SetArgs_SeparatorAlone_RunsWithoutArguments()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                await h.SetArgs().ExecuteAsync(new[] { "byteshelf", "--" });

                Assert.Contains("/bin/byteshelf\"\n", await File.ReadAllTextAsync(h.Scope.GetUnitFilePath("byteshelf")));
            }
        }

        [Fact]
        public async Task SetArgs_WithoutSeparator_RestoresTheManifestArguments()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                InstalledApp app = (await h.Config.GetAppAsync("byteshelf"))!;
                app.Args = new List<string> { "serve" };
                await h.Config.SaveAppAsync(app);

                await h.SetArgs().ExecuteAsync(new[] { "byteshelf" });

                Assert.Contains("/bin/byteshelf\" \"sync\" \"run\"\n", await File.ReadAllTextAsync(h.Scope.GetUnitFilePath("byteshelf")));
                Assert.Null((await h.Config.GetAppAsync("byteshelf"))!.Args);
            }
        }

        [Fact]
        public async Task SetArgs_AppWithoutService_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/tool", "1.0.0", """{ "executable": "tool", "command": "tool" }""", executable: "tool");
                await h.Install().ExecuteAsync(new[] { "tool", "--from", "github:owner/tool" });

                int exitCode = await h.SetArgs().ExecuteAsync(new[] { "tool", "--", "x" });

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains("not a service"));
            }
        }

        [Fact]
        public async Task SetArgs_ArgumentWithALineBreak_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                int exitCode = await h.SetArgs().ExecuteAsync(new[] { "byteshelf", "--", "run\nUser=root" });

                Assert.Equal(1, exitCode);
                Assert.DoesNotContain("User=root", await File.ReadAllTextAsync(h.Scope.GetUnitFilePath("byteshelf")));
            }
        }

        public static TheoryData<Func<AppHarness, ICommand>, string[], string> InvalidArguments => new TheoryData<Func<AppHarness, ICommand>, string[], string>
        {
            { h => h.Set(), new[] { "byteshelf" }, "Usage: updaemon set" },
            { h => h.Set(), new[] { "missing", "A=1" }, "'missing' is not installed" },
            { h => h.Set(), new[] { "byteshelf", "A=1", "--bogus" }, "Unknown option '--bogus'" },
            { h => h.Unset(), new[] { "byteshelf" }, "Usage: updaemon unset" },
            { h => h.Unset(), new[] { "missing", "A" }, "'missing' is not installed" },
            { h => h.SetArgs(), new[] { "byteshelf", "serve" }, "Usage: updaemon set-args" },
            { h => h.SetArgs(), new[] { "missing" }, "'missing' is not installed" },
        };

        [Theory]
        [MemberData(nameof(InvalidArguments))]
        public async Task InvalidArguments_Fail(Func<AppHarness, ICommand> command, string[] args, string expectedError)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                int exitCode = await command(h).ExecuteAsync(args, CancellationToken.None);

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains(expectedError));
            }
        }
    }
}
