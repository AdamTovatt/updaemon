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
                h.Timer.CurrentInterval = "1h";
                h.Output.Clear();

                int exitCode = await h.Status().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.Equal(
                    new[]
                    {
                        "updaemon 1.0.0, user scope",
                        "Update timer: 1h",
                        "Self-update: on",
                        "",
                        "byteshelf  1.0.0  command + service  running  github:owner/byteshelf",
                        "tool       2.0.0  command            -        github:owner/tool       skips 2.1.0",
                    },
                    h.Output.Messages);
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
