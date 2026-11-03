using Updaemon.Configuration;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    /// <summary>
    /// Tests the parts of <see cref="MacTimerManager"/> that don't require launchctl —
    /// plist content generation and StartInterval parsing. The full SetTimerAsync /
    /// DisableTimerAsync flows are validated by hand on macOS (see RELEASE_GUIDE.md).
    /// </summary>
    public class MacTimerManagerTests
    {
        [Fact]
        public void BuildPlist_HasExpectedShape()
        {
            string plist = MacTimerManager.BuildPlist("com.updaemon", "/usr/local/bin/updaemon", 600);

            Assert.Contains("<plist", plist);
            Assert.Contains("<key>Label</key>", plist);
            Assert.Contains("<string>com.updaemon</string>", plist);
            Assert.Contains("<key>ProgramArguments</key>", plist);
            Assert.Contains("<string>/usr/local/bin/updaemon</string>", plist);
            Assert.Contains("<string>update</string>", plist);
            Assert.Contains("<key>StartInterval</key>", plist);
            Assert.Contains("<integer>600</integer>", plist);
            Assert.Contains("<key>RunAtLoad</key>", plist);
            Assert.Contains("<false/>", plist);
        }

        [Fact]
        public async Task GetCurrentIntervalAsync_NoFile_ReturnsNull()
        {
            using TempFileHelper tempHelper = new TempFileHelper();
            UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory, isMacOS: true);
            string plistPath = Path.Combine(scope.UnitDirectory, scope.TimerLabel + ".plist");
            Directory.CreateDirectory(scope.UnitDirectory);

            MacTimerManager manager = new MacTimerManager(new MockOutputWriter(), scope);

            string? interval = await manager.GetCurrentIntervalAsync();

            Assert.Null(interval);
        }

        [Theory]
        [InlineData(30, "30s")]
        [InlineData(300, "5m")]
        [InlineData(3600, "1h")]
        [InlineData(7200, "2h")]
        [InlineData(45, "45s")]
        public async Task GetCurrentIntervalAsync_FormatsHumanReadable(int seconds, string expected)
        {
            using TempFileHelper tempHelper = new TempFileHelper();
            UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory, isMacOS: true);
            string plistPath = Path.Combine(scope.UnitDirectory, scope.TimerLabel + ".plist");
            Directory.CreateDirectory(scope.UnitDirectory);

            string plist = MacTimerManager.BuildPlist(scope.TimerLabel, "/usr/local/bin/updaemon", seconds);
            await File.WriteAllTextAsync(plistPath, plist);

            MacTimerManager manager = new MacTimerManager(new MockOutputWriter(), scope);

            string? interval = await manager.GetCurrentIntervalAsync();

            Assert.Equal(expected, interval);
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

            string? interval = await manager.GetCurrentIntervalAsync();

            Assert.Null(interval);
        }
    }
}
