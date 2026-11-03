using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    /// <summary>
    /// The commands 'updaemon logs' runs, and running them on the terminal.
    /// </summary>
    public class ServiceLogTests
    {
        [Fact]
        public void JournalctlArguments_UserScope_LastLinesOfTheUsersUnit()
        {
            Assert.Equal(
                new[] { "--user", "--unit", "app", "--lines", "100", "--no-pager" },
                LinuxServiceManager.JournalctlArguments(TestScope.Create("/r"), "app", follow: false));
        }

        [Fact]
        public void JournalctlArguments_SystemScopeFollowing_Follows()
        {
            Assert.Equal(
                new[] { "--unit", "app", "--lines", "100", "--no-pager", "--follow" },
                LinuxServiceManager.JournalctlArguments(TestScope.Create("/r", isSystem: true), "app", follow: true));
        }

        [Fact]
        public void TailArguments_Following_FollowsBothFiles()
        {
            Assert.Equal(new[] { "-f", "-n", "100", "a.log", "a.err.log" }, MacServiceManager.TailArguments(new[] { "a.log", "a.err.log" }, follow: true));
        }

        [Fact]
        public async Task MacShowLogsAsync_NoLogFileYet_SaysSo()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                MockOutputWriter output = new MockOutputWriter();
                MacServiceManager manager = new MacServiceManager(TestScope.Create(temp.TempDirectory, isMacOS: true), output);

                int exitCode = await manager.ShowLogsAsync("app", follow: false);

                Assert.Equal(0, exitCode);
                Assert.Equal(new[] { "app has not written any log yet." }, output.Messages);
            }
        }

        [Fact]
        public async Task RunAttachedAsync_CancelledLikeCtrlC_EndsNormally()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
            {
                Assert.Equal(0, await ProcessRunner.RunAttachedAsync("sleep", new[] { "10" }, cancellation.Token));
            }
        }
    }
}
