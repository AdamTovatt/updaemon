using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    /// <summary>
    /// Reading a service's state from what systemctl and launchctl print.
    /// </summary>
    public class ServiceStateParsingTests
    {
        [Theory]
        [InlineData("ActiveState=active\nNRestarts=2\n", true, 2)]
        [InlineData("NRestarts=0\nActiveState=activating\n", false, 0)]
        [InlineData("ActiveState=active\n", true, 0)]
        [InlineData("", false, 0)]
        public void LinuxParseState_SystemctlShowOutput_ReadsStateAndRestarts(string output, bool isActive, int starts)
        {
            Assert.Equal(new ServiceState(isActive, starts), LinuxServiceManager.ParseState(output));
        }

        [Theory]
        [InlineData("Linger=yes\n", true)]
        [InlineData("Linger=no\n", false)]
        [InlineData("", false)]
        public void LinuxLingerIsOn_LoginctlShowUserOutput_IsOnOnlyForYes(string output, bool expected)
        {
            Assert.Equal(expected, LinuxServiceManager.LingerIsOn(output));
        }

        [Fact]
        public async Task LinuxServicesStopAtLogout_SystemScope_NeverWithoutAsking()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                LinuxServiceManager manager = new LinuxServiceManager(TestScope.Create(temp.TempDirectory, isSystem: true));

                Assert.False(await manager.ServicesStopAtLogoutAsync());
            }
        }

        [Theory]
        [InlineData("gui/501/com.updaemon.app = {\n\tactive count = 1\n\tstate = running\n\truns = 3\n}\n", true, 3)]
        [InlineData("gui/501/com.updaemon.app = {\n\tstate = not running\n\truns = 7\n\tlast exit code = 1\n}\n", false, 7)]
        [InlineData("gui/501/com.updaemon.app = {\n\tstate = running\n}\n", true, 0)]
        public void MacParseState_LaunchctlPrintOutput_ReadsStateAndRuns(string output, bool isActive, int starts)
        {
            Assert.Equal(new ServiceState(isActive, starts), MacServiceManager.ParseState(output));
        }

        [Theory]
        [InlineData(0, "enabled\n", "", true)]
        [InlineData(0, "enabled-runtime\n", "", true)]
        [InlineData(1, "disabled\n", "", false)]
        [InlineData(4, "not-found\n", "", null)]
        [InlineData(1, "", "Failed to get unit file state for app.service: No such file or directory\n", null)]
        public void LinuxReadIsEnabled_WhatSystemctlSays_IsRead(int exitCode, string stdOut, string stdErr, bool? enabled)
        {
            Assert.Equal(enabled, LinuxServiceManager.ReadIsEnabled("app.service", new ProcessRunner.Result(exitCode, stdOut, stdErr)));
        }

        [Theory]
        [InlineData(1, "", "Failed to connect to bus: No medium found\n", "Failed to connect to bus: No medium found")]
        [InlineData(1, "masked\n", "", "masked")]
        public void LinuxReadIsEnabled_AnythingElse_ThrowsWithWhatSystemctlSaid(int exitCode, string stdOut, string stdErr, string said)
        {
            // Read as disabled, it would skip a restart in silence.
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => LinuxServiceManager.ReadIsEnabled("app.service", new ProcessRunner.Result(exitCode, stdOut, stdErr)));

            Assert.Equal($"'systemctl is-enabled app.service' could not tell whether it is enabled: {said}", ex.Message);
        }
    }
}
