using Updaemon.Models;
using Updaemon.Services;

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
        [InlineData("gui/501/com.updaemon.app = {\n\tactive count = 1\n\tstate = running\n\truns = 3\n}\n", true, 3)]
        [InlineData("gui/501/com.updaemon.app = {\n\tstate = not running\n\truns = 7\n\tlast exit code = 1\n}\n", false, 7)]
        [InlineData("gui/501/com.updaemon.app = {\n\tstate = running\n}\n", true, 0)]
        public void MacParseState_LaunchctlPrintOutput_ReadsStateAndRuns(string output, bool isActive, int starts)
        {
            Assert.Equal(new ServiceState(isActive, starts), MacServiceManager.ParseState(output));
        }
    }
}
