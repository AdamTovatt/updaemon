using Updaemon.Commands;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    /// <summary>
    /// The read-only commands: status and logs.
    /// </summary>
    public class StatusAndLogsCommandTests
    {
        [Fact]
        public async Task Status_ShowsUpdaemonThenOneAlignedLinePerApp()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.AddRelease("owner/tool", "2.0.0", """{ "executable": "tool", "command": "tool" }""", executable: "tool");
                await h.Install().ExecuteAsync(new[] { "tool", "--from", "github:owner/tool" });
                await h.SkipAsync("tool", new Version(2, 1, 0));
                h.Timer.IsEnabled = true;
                h.Timer.CurrentInterval = TimeSpan.FromHours(1);
                await h.RunLog.SaveAsync(new UpdateRun(h.Time.GetUtcNow() - TimeSpan.FromMinutes(150), UpdateStarter.Timer, new[] { new UpdateFailure("tool", "checksum mismatch. 2.0.0 stays.") }));
                h.Output.Clear();

                int exitCode = await h.Status().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.Equal(
                    new[]
                    {
                        "updaemon 1.0.0, user scope",
                        "Update timer: 1h",
                        "Self-update: on",
                        "Last update: 2h ago by the timer, 1 failed (tool: checksum mismatch)",
                        "",
                        "byteshelf  1.0.0  command + service  running  github:owner/byteshelf",
                        "tool       2.0.0  command            -        github:owner/tool       skips 2.1.0",
                    },
                    h.Output.Messages);
            }
        }

        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        public static TheoryData<UpdateRun?, string> Runs => new TheoryData<UpdateRun?, string>
        {
            { null, "never" },
            { new UpdateRun(Now.AddSeconds(-30), UpdateStarter.Person, Array.Empty<UpdateFailure>()), "just now by hand, nothing failed" },
            { new UpdateRun(Now.AddMinutes(-59), UpdateStarter.Timer, Array.Empty<UpdateFailure>()), "59m ago by the timer, nothing failed" },
            {
                new UpdateRun(Now.AddDays(-3), UpdateStarter.Timer, new[]
                {
                    new UpdateFailure("byteshelf", "app.zip does not match its SHA-256 in SHA256SUMS; the download is corrupt."),
                    new UpdateFailure("updaemon", "updating to 1.2.0 failed: no space left on device. 1.1.0 stays."),
                    new UpdateFailure("tool", "Invalid updaemon.json:\n  'executable' is missing"),
                }),
                "3d ago by the timer, 3 failed (byteshelf: app.zip does not match its SHA-256 in SHA256SUMS; the download is corrupt; " +
                "updaemon: updating to 1.2.0 failed: no space left on device; tool: Invalid updaemon.json:)"
            },
        };

        [Theory]
        [MemberData(nameof(Runs))]
        public void DescribeRun_SaysWhenWhoAndWhatFailed(UpdateRun? run, string expected)
        {
            Assert.Equal(expected, StatusCommand.DescribeRun(run, Now));
        }

        [Fact]
        public async Task Status_RunRecordUnreadable_SaysSoAndShowsTheRest()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                File.WriteAllText(h.Scope.LastUpdateFilePath, "{ not json");
                h.Output.Clear();

                int exitCode = await h.Status().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.Contains(h.Output.Messages, line => line.StartsWith($"Last update: unknown ({h.Scope.LastUpdateFilePath} cannot be read: "));
                Assert.StartsWith("byteshelf", h.Output.Messages[^1]);
            }
        }

        [Fact]
        public async Task Status_ServicesStopAtLogout_WarnsNamingTheCommand()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();
                h.ServiceManager.ServicesStopAtLogout = true;
                h.Output.Clear();

                await h.Status().ExecuteAsync(Array.Empty<string>());

                string user = Environment.UserName;
                Assert.Equal(
                    new[] { $"Warning: lingering is off for {user}, so your services stop when you log out. To keep them running, run: sudo loginctl enable-linger {user}" },
                    h.Output.Errors);
            }
        }

        [Fact]
        public async Task Status_ServicesStopAtLogoutButThereIsNoService_DoesNotWarn()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/rg", "1.0.0", """{ "executable": "bin/byteshelf", "command": "rg" }""");
                await h.Install().ExecuteAsync(new[] { "rg", "--from", "github:owner/rg" });
                h.ServiceManager.ServicesStopAtLogout = true;
                h.Output.Clear();

                await h.Status().ExecuteAsync(Array.Empty<string>());

                Assert.Empty(h.Output.Errors);
            }
        }

        [Fact]
        public async Task Status_ServicesKeepRunningAtLogout_DoesNotWarn()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.Status().ExecuteAsync(Array.Empty<string>());

                Assert.Empty(h.Output.Errors);
            }
        }

        [Fact]
        public async Task Status_AppWithoutCurrentVersion_IsShownAsSuch()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.Config.SaveAppAsync(new InstalledApp { Name = "orphan", Plugin = "github", Remote = "o/r" });

                int exitCode = await h.Status().ExecuteAsync(new[] { "orphan" });

                Assert.Equal(0, exitCode);
                Assert.Contains("orphan  no version  ?  -  github:o/r", h.Output.Messages);
            }
        }

        [Fact]
        public async Task Status_TimerOnWithoutAReadableInterval_SaysOn()
        {
            using (AppHarness h = new AppHarness())
            {
                h.Timer.IsEnabled = true;
                h.Timer.CurrentInterval = null;

                await h.Status().ExecuteAsync(Array.Empty<string>());

                Assert.Contains("Update timer: on (interval unknown)", h.Output.Messages);
            }
        }

        [Fact]
        public async Task Logs_Service_ShowsItsLog()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                int exitCode = await h.Logs().ExecuteAsync(new[] { "byteshelf", "-f" });

                Assert.Equal(0, exitCode);
                Assert.Contains(MockServiceManager.Call(nameof(IServiceManager.ShowLogsAsync), "byteshelf:True"), h.ServiceManager.MethodCalls);
            }
        }

        [Fact]
        public async Task Logs_CommandOnly_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                h.AddRelease("owner/tool", "1.0.0", """{ "executable": "tool", "command": "tool" }""", executable: "tool");
                await h.Install().ExecuteAsync(new[] { "tool", "--from", "github:owner/tool" });

                int exitCode = await h.Logs().ExecuteAsync(new[] { "tool" });

                Assert.Equal(1, exitCode);
                Assert.Contains(h.Output.Errors, e => e.Contains("tool is not a service"));
            }
        }
    }
}
