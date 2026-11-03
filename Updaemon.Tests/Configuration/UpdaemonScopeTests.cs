using Updaemon.Configuration;
using Updaemon.IO;

namespace Updaemon.Tests.Configuration
{
    public class UpdaemonScopeTests
    {
        private static readonly HostPlatform Linux = new HostPlatform(false, "linux-arm64");
        private static readonly HostPlatform Mac = new HostPlatform(true, "osx-arm64");

        [Fact]
        public void ForUser_Linux_UsesXdgDirectoriesWhenSet()
        {
            UpdaemonScope scope = UpdaemonScope.ForUser(Linux, 1000, "ada", "/home/ada", "/data", "/conf");

            Assert.False(scope.IsSystem);
            Assert.Equal("user", scope.Name);
            Assert.Equal("/data/updaemon", scope.RootDirectory);
            Assert.Equal("/data/updaemon/apps", scope.AppsDirectory);
            Assert.Equal("/home/ada/.local/bin", scope.BinDirectory);
            Assert.Equal("/conf/systemd/user", scope.UnitDirectory);
            Assert.Equal("/conf/systemd/user/byteshelf.service", scope.GetUnitFilePath("byteshelf"));
            Assert.Equal(new[] { "--user" }, scope.SystemdScopeArguments);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("relative/path")]
        public void ForUser_Linux_FallsBackToHomeWhenXdgUnsetOrRelative(string? xdg)
        {
            UpdaemonScope scope = UpdaemonScope.ForUser(Linux, 1000, "ada", "/home/ada", xdg, xdg);

            Assert.Equal("/home/ada/.local/share/updaemon", scope.RootDirectory);
            Assert.Equal("/home/ada/.config/systemd/user", scope.UnitDirectory);
        }

        [Fact]
        public void ForUser_Mac_UsesLibraryDirectoriesAndGuiDomain()
        {
            UpdaemonScope scope = UpdaemonScope.ForUser(Mac, 501, "ada", "/Users/ada", null, null);

            Assert.Equal("/Users/ada/Library/Application Support/updaemon", scope.RootDirectory);
            Assert.Equal("/Users/ada/.local/bin", scope.BinDirectory);
            Assert.Equal("/Users/ada/Library/LaunchAgents/com.updaemon.byteshelf.plist", scope.GetUnitFilePath("byteshelf"));
            Assert.Equal("/Users/ada/Library/Logs/updaemon", scope.LogDirectory);
            Assert.Equal("gui/501", scope.LaunchdDomain);
            Assert.Equal("com.updaemon", scope.TimerLabel);
        }

        [Fact]
        public void ForSystem_KeepsTheMachineWidePaths()
        {
            UpdaemonScope linux = UpdaemonScope.ForSystem(Linux, null);
            UpdaemonScope mac = UpdaemonScope.ForSystem(Mac, null);

            Assert.Equal("system", linux.Name);
            Assert.Equal("/var/lib/updaemon", linux.RootDirectory);
            Assert.Equal("/opt/byteshelf/current", linux.GetCurrentLinkPath("byteshelf"));
            Assert.Equal("/usr/local/bin/byteshelf", linux.GetCommandPath("byteshelf"));
            Assert.Equal("/etc/systemd/system/byteshelf.service", linux.GetUnitFilePath("byteshelf"));
            Assert.Empty(linux.SystemdScopeArguments);
            Assert.Equal("updaemon", linux.TimerLabel);

            Assert.Equal("/usr/local/var/updaemon", mac.RootDirectory);
            Assert.Equal("/usr/local/opt", mac.AppsDirectory);
            Assert.Equal("/Library/LaunchDaemons/com.updaemon.byteshelf.plist", mac.GetUnitFilePath("byteshelf"));
            Assert.Equal("system", mac.LaunchdDomain);
        }

        [Fact]
        public void Announcement_NamesTheScopeAndHowToGetTheOther()
        {
            Assert.Equal("For user ada only (run with sudo for the whole machine).", UpdaemonScope.ForUser(Linux, 1000, "ada", "/home/ada", null, null).Announcement);
            Assert.Equal("For the whole machine (run without sudo for user ada only).", UpdaemonScope.ForSystem(Linux, "ada").Announcement);
            Assert.Equal("For the whole machine (run without sudo for your own user only).", UpdaemonScope.ForSystem(Linux, null).Announcement);
        }

        [Fact]
        public void DetectOther_FromAUserScope_IsTheSystemScope()
        {
            UpdaemonScope? other = UpdaemonScope.DetectOther(UpdaemonScope.ForUser(Linux, 1000, "ada", "/home/ada", null, null));

            Assert.Equal(UpdaemonScope.ForSystem(Linux, "ada"), other);
        }

        [Fact]
        public void DetectOther_UnderSudo_IsTheScopeOfTheUserWhoRanIt()
        {
            // The user running the tests stands in for the one who ran sudo. Some sandboxes have no user
            // database entry for it; there is then no scope to find.
            string userName = Environment.UserName;
            LibC.User? user = LibC.FindUser(userName);

            UpdaemonScope? other = UpdaemonScope.DetectOther(UpdaemonScope.ForSystem(Linux, userName));

            Assert.Equal(user == null ? null : UpdaemonScope.ForUser(Linux, user.Id, userName, user.Home, null, null), other);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("no-such-user-updaemon")]
        public void DetectOther_UnderSudoWithoutAKnownUser_IsNull(string? sudoUser)
        {
            Assert.Null(UpdaemonScope.DetectOther(UpdaemonScope.ForSystem(Linux, sudoUser)));
        }
    }
}
