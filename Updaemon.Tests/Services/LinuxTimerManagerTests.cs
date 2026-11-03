using Updaemon.Configuration;
using Updaemon.Services;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    /// <summary>
    /// The parts of <see cref="LinuxTimerManager"/> that need no systemctl: the generated units and
    /// reading the interval back.
    /// </summary>
    public class LinuxTimerManagerTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BuildServiceUnit_RunsTheScopesUpdaemonCommand(bool isSystem)
        {
            UpdaemonScope scope = TestScope.Create("/t", isSystem);

            string unit = LinuxTimerManager.BuildServiceUnit(scope.UpdaemonCommandPath);

            Assert.Contains($"ExecStart=\"{scope.BinDirectory}/updaemon\" update\n", unit);
        }

        [Theory]
        [InlineData(30, "*:*:0/30")]
        [InlineData(300, "*:0/5:00")]
        [InlineData(7200, "0/2:00:00")]
        public void BuildTimerUnit_ConvertsTheIntervalToACalendarExpression(int seconds, string expected)
        {
            string unit = LinuxTimerManager.BuildTimerUnit(TimeSpan.FromSeconds(seconds));

            Assert.Contains($"OnCalendar={expected}\n", unit);
        }

        [Fact]
        public async Task FindZeroXTimerAsync_FindsNoneAsTheTimerUnitIsWhatUpdaemonZeroXWrote()
        {
            const string zeroXTimerUnit = "[Unit]\nDescription=Run updaemon update periodically\n\n[Timer]\nOnCalendar=0/1:00:00\nPersistent=true\n\n[Install]\nWantedBy=timers.target\n";

            Assert.Equal(zeroXTimerUnit, LinuxTimerManager.BuildTimerUnit(TimeSpan.FromHours(1)));
            Assert.Null(await new LinuxTimerManager(TestScope.Create("/t", isSystem: true)).FindZeroXTimerAsync());
        }

        [Fact]
        public async Task GetCurrentIntervalAsync_ReadsTheTimerUnit()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory);
                Directory.CreateDirectory(scope.UnitDirectory);
                await File.WriteAllTextAsync(Path.Combine(scope.UnitDirectory, scope.TimerLabel + ".timer"), LinuxTimerManager.BuildTimerUnit(TimeSpan.FromMinutes(5)));

                string? interval = await new LinuxTimerManager(scope).GetCurrentIntervalAsync();

                Assert.Equal("*:0/5:00", interval);
            }
        }
    }
}
