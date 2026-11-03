using Updaemon.Configuration;

namespace Updaemon.Tests.Configuration
{
    public class UpdaemonScopeTests
    {
        private static readonly HostPlatform Linux = new HostPlatform(false, "linux-arm64");
        private static readonly HostPlatform Mac = new HostPlatform(true, "osx-arm64");

        [Fact]
        public void ForUser_Linux_UsesXdgDirectoriesWhenSet()
        {
            UpdaemonScope scope = UpdaemonScope.ForUser(Linux, 1000, "/home/ada", "/data", "/conf");

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
            UpdaemonScope scope = UpdaemonScope.ForUser(Linux, 1000, "/home/ada", xdg, xdg);

            Assert.Equal("/home/ada/.local/share/updaemon", scope.RootDirectory);
            Assert.Equal("/home/ada/.config/systemd/user", scope.UnitDirectory);
        }

        [Fact]
        public void ForUser_Mac_UsesLibraryDirectoriesAndGuiDomain()
        {
            UpdaemonScope scope = UpdaemonScope.ForUser(Mac, 501, "/Users/ada", null, null);

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
            UpdaemonScope linux = UpdaemonScope.ForSystem(Linux);
            UpdaemonScope mac = UpdaemonScope.ForSystem(Mac);

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
    }
}
