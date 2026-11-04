using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    /// <summary>What the Linux and macOS service managers say when a user's manager cannot be reached, and where they look for it.</summary>
    public class ServiceManagerReachabilityTests
    {
        [Fact]
        public void LinuxUnreachable_NamesWhyAndTheTwoWaysToHaveTheManagerRun()
        {
            Assert.Equal(
                "Cannot reach systemd's user manager for ada (Failed to connect to bus: No medium found). It runs while ada is logged in; " +
                "to have it run always, run: sudo loginctl enable-linger ada",
                LinuxServiceManager.Unreachable("ada", "Failed to connect to bus: No medium found.").Message);
        }

        [Theory]
        [InlineData(null, true, "/run/user/1000")]
        [InlineData("/run/user/0", true, "/run/user/1000")]
        [InlineData("/run/user/1000", true, null)]
        [InlineData(null, false, null)]
        public void LinuxUserRuntimeDirectory_SetOnlyWhenMissingOrAnothersAndTheUsersExists(string? current, bool ownExists, string? expected)
        {
            Assert.Equal(expected, LinuxServiceManager.UserRuntimeDirectory(current, 1000, path => ownExists && path == "/run/user/1000"));
        }

        [Fact]
        public void MacUnreachable_NamesTheDomainAndTheDesktopLogin()
        {
            Assert.Equal(
                "Cannot reach launchd's gui/501 domain for ada, which exists only while ada is logged in to the Mac's desktop. Log in as ada on the Mac itself, then try again.",
                MacServiceManager.Unreachable("ada", "gui/501").Message);
        }

        [Fact]
        public async Task SystemScope_IsReachableWithoutAsking()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                // No systemctl or launchctl runs here: neither exists on every machine the tests run on.
                Assert.NotNull(await new LinuxServiceManager(TestScope.Create(temp.TempDirectory, isSystem: true)).EnsureReachableAsync());
                Assert.NotNull(await new MacServiceManager(TestScope.Create(temp.TempDirectory, isSystem: true, isMacOS: true), new MockOutputWriter()).EnsureReachableAsync());
            }
        }
    }
}
