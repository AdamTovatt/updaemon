using Updaemon.Configuration;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    /// <summary>
    /// Tests the parts of <see cref="MacTimerManager"/> that don't require launchctl:
    /// plist content generation and StartInterval parsing. The full SetTimerAsync /
    /// RemoveTimerAsync flows need launchctl, so they are not run here.
    /// </summary>
    public class MacTimerManagerTests
    {
        [Fact]
        public void BuildPlist_HasExpectedShape()
        {
            string plist = MacTimerManager.BuildPlist("com.updaemon", "/usr/local/bin/updaemon", 600, "/var/log/updaemon/updaemon.log");

            Assert.Contains("<plist", plist);
            Assert.Contains("<key>Label</key>", plist);
            Assert.Contains("<string>com.updaemon</string>", plist);
            Assert.Contains("<key>ProgramArguments</key>", plist);
            Assert.Contains("<string>/usr/local/bin/updaemon</string>", plist);
            Assert.Contains("<string>update</string>\n        <string>--timer</string>", plist);
            Assert.Contains("<key>StartInterval</key>", plist);
            Assert.Contains("<integer>600</integer>", plist);
            Assert.Contains("<key>RunAtLoad</key>", plist);
            Assert.Contains("<false/>", plist);
            Assert.Contains("<key>StandardOutPath</key>\n    <string>/var/log/updaemon/updaemon.log</string>", plist);
            Assert.Contains("<key>StandardErrorPath</key>\n    <string>/var/log/updaemon/updaemon.log</string>", plist);
        }

        [Fact]
        public async Task GetCurrentIntervalAsync_NoFile_ReturnsNull()
        {
            using TempFileHelper tempHelper = new TempFileHelper();
            UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory, isMacOS: true);
            string plistPath = Path.Combine(scope.UnitDirectory, scope.TimerLabel + ".plist");
            Directory.CreateDirectory(scope.UnitDirectory);

            MacTimerManager manager = new MacTimerManager(new MockOutputWriter(), scope);

            TimeSpan? interval = await manager.GetCurrentIntervalAsync();

            Assert.Null(interval);
        }

        [Theory]
        [InlineData(45)]
        [InlineData(7200)]
        public async Task GetCurrentIntervalAsync_ReadsThePlist(int seconds)
        {
            using TempFileHelper tempHelper = new TempFileHelper();
            UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory, isMacOS: true);
            string plistPath = Path.Combine(scope.UnitDirectory, scope.TimerLabel + ".plist");
            Directory.CreateDirectory(scope.UnitDirectory);

            string plist = MacTimerManager.BuildPlist(scope.TimerLabel, "/usr/local/bin/updaemon", seconds, scope.UpdateLogPath);
            await File.WriteAllTextAsync(plistPath, plist);

            MacTimerManager manager = new MacTimerManager(new MockOutputWriter(), scope);

            TimeSpan? interval = await manager.GetCurrentIntervalAsync();

            Assert.Equal(TimeSpan.FromSeconds(seconds), interval);
        }

        [Fact]
        public async Task GetCurrentIntervalAsync_PlistMissingStartInterval_ReturnsNull()
        {
            using TempFileHelper tempHelper = new TempFileHelper();
            UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory, isMacOS: true);
            string plistPath = Path.Combine(scope.UnitDirectory, scope.TimerLabel + ".plist");
            Directory.CreateDirectory(scope.UnitDirectory);
            await File.WriteAllTextAsync(plistPath, "<?xml version=\"1.0\"?><plist><dict></dict></plist>");

            MacTimerManager manager = new MacTimerManager(new MockOutputWriter(), scope);

            TimeSpan? interval = await manager.GetCurrentIntervalAsync();

            Assert.Null(interval);
        }
    }
}
