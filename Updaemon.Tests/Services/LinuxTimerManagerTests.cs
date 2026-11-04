using Updaemon.Configuration;
using Updaemon.Conversion;
using Updaemon.Models;
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

            Assert.Contains($"ExecStart=\"{scope.BinDirectory}/updaemon\" update --timer\n", unit);
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
        public async Task FindZeroXTimerAsync_NoTimerUnit_FindsNone()
        {
            Assert.Null(await new LinuxTimerManager(TestScope.Create("/t", isSystem: true)).FindZeroXTimerAsync());
        }

        private const string ZeroXTimerUnit = "[Unit]\nDescription=Run updaemon update periodically\n\n[Timer]\nOnCalendar=0/1:00:00\nPersistent=true\n\n[Install]\nWantedBy=timers.target\n";

        private const string ZeroXServiceUnit = "[Unit]\nDescription=Updaemon update service\n\n[Service]\nType=oneshot\nExecStart=\"/t/bin/updaemon\" update\n";

        [Fact]
        public void ZeroXTimerFrom_ZeroXUnitsThatAreOn_AreRewrittenKeepingTheirInterval()
        {
            UpdaemonScope scope = TestScope.Create("/t", isSystem: true);

            ZeroXTimer? timer = new LinuxTimerManager(scope).ZeroXTimerFrom(enabled: true, ZeroXTimerUnit, ZeroXServiceUnit);

            Assert.Equal(BothUnits(scope), timer!.Files);
            Assert.True(timer.IsOn);
            Assert.Equal(TimeSpan.FromHours(1), timer.Interval);
        }

        private static string[] BothUnits(UpdaemonScope scope) =>
            [Path.Combine(scope.UnitDirectory, "updaemon.timer"), Path.Combine(scope.UnitDirectory, "updaemon.service")];

        [Fact]
        public void ZeroXTimerFrom_TurnedOff_IsFoundOffToBeRemoved()
        {
            UpdaemonScope scope = TestScope.Create("/t", isSystem: true);

            ZeroXTimer? timer = new LinuxTimerManager(scope).ZeroXTimerFrom(enabled: false, ZeroXTimerUnit, ZeroXServiceUnit);

            Assert.Equal(BothUnits(scope), timer!.Files);
            Assert.False(timer.IsOn);
            Assert.Null(timer.Interval);
        }

        [Fact]
        public void ZeroXTimerFrom_ThisUpdaemonsOwn_IsLeftAlone()
        {
            UpdaemonScope scope = TestScope.Create("/t", isSystem: true);

            Assert.Null(new LinuxTimerManager(scope).ZeroXTimerFrom(enabled: true, ZeroXTimerUnit, LinuxTimerManager.BuildServiceUnit(scope.UpdaemonCommandPath)));
        }

        [Fact]
        public void ZeroXTimerFrom_OnWithAnIntervalItCannotRead_IsLeftAlone()
        {
            UpdaemonScope scope = TestScope.Create("/t", isSystem: true);

            Assert.Null(new LinuxTimerManager(scope).ZeroXTimerFrom(enabled: true, ZeroXTimerUnit.Replace("0/1:00:00", "daily"), ZeroXServiceUnit));
        }

        [Fact]
        public async Task RemoveZeroXTimerAsync_TimerThatIsOn_KeepsTheUnitsSetTimerRewrote()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(temp.TempDirectory, isSystem: true);
                Directory.CreateDirectory(scope.UnitDirectory);
                foreach (string unit in BothUnits(scope))
                {
                    await File.WriteAllTextAsync(unit, "rewritten");
                }

                await new LinuxTimerManager(scope).RemoveZeroXTimerAsync(new ZeroXTimer(BothUnits(scope), IsOn: true, TimeSpan.FromHours(1)), new ReachableServiceManager());

                Assert.All(BothUnits(scope), unit => Assert.Equal("rewritten", File.ReadAllText(unit)));
            }
        }

        [Theory]
        [InlineData(30)]
        [InlineData(300)]
        [InlineData(7200)]
        public void ReadInterval_ReadsBackWhatBuildTimerUnitWrote(int seconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(seconds), LinuxTimerManager.ReadInterval(LinuxTimerManager.BuildTimerUnit(TimeSpan.FromSeconds(seconds))));
        }

        [Fact]
        public async Task GetCurrentIntervalAsync_ReadsTheTimerUnit()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory);
                Directory.CreateDirectory(scope.UnitDirectory);
                await File.WriteAllTextAsync(Path.Combine(scope.UnitDirectory, scope.TimerLabel + ".timer"), LinuxTimerManager.BuildTimerUnit(TimeSpan.FromMinutes(5)));

                TimeSpan? interval = await new LinuxTimerManager(scope).GetCurrentIntervalAsync();

                Assert.Equal(TimeSpan.FromMinutes(5), interval);
            }
        }
    }
}
