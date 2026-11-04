using Updaemon.Commands;
using Updaemon.Configuration;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public sealed class TimerCommandTests : IDisposable
    {
        private readonly TempFileHelper _temp = new TempFileHelper();
        private readonly MockTimerManager _timer = new MockTimerManager();
        private readonly MockServiceManager _serviceManager = new MockServiceManager();
        private readonly ConfigManager _config;

        public TimerCommandTests()
        {
            _config = new ConfigManager(TestScope.Create(_temp.TempDirectory));
        }

        public void Dispose() => _temp.Dispose();

        private async Task<(int ExitCode, MockOutputWriter Output)> RunAsync(params string[] args)
        {
            MockOutputWriter output = new MockOutputWriter();
            int exitCode = await new TimerCommand(_timer, _serviceManager, _config, output).ExecuteAsync(args);
            return (exitCode, output);
        }

        [Fact]
        public async Task Keep_NoTimerSetBefore_SetsIt()
        {
            (int exitCode, MockOutputWriter output) = await RunAsync("1h", "--keep");

            Assert.Equal(0, exitCode);
            Assert.Equal(TimeSpan.FromHours(1), _timer.LastSetInterval);
            Assert.Contains("The update timer runs 'updaemon update' every 1h. Turn it off with 'updaemon timer -'.", output.Messages);
        }

        [Theory]
        [InlineData("1h", "--keep")]
        [InlineData("-")]
        public async Task NoUserServiceManager_ChangesNothing(params string[] args)
        {
            _serviceManager.Unreachable = "Cannot reach systemd's user manager.";

            ServiceManagerUnreachableException ex = await Assert.ThrowsAsync<ServiceManagerUnreachableException>(() => RunAsync(args));

            Assert.Equal("Cannot reach systemd's user manager.", ex.Message);
            Assert.Null(_timer.LastSetInterval);
            Assert.Empty(_timer.MethodCalls);
            Assert.False((await _config.LoadConfigAsync()).TimerTurnedOff);
        }

        [Fact]
        public async Task Keep_TimerSet_KeepsItsInterval()
        {
            Assert.Equal(0, (await RunAsync("2h")).ExitCode);

            (int exitCode, MockOutputWriter output) = await RunAsync("1h", "--keep");

            Assert.Equal(0, exitCode);
            Assert.Equal(TimeSpan.FromHours(2), _timer.LastSetInterval);
            Assert.Equal(new[] { "The update timer stays as it was (2h). Turn it off with 'updaemon timer -'." }, output.Messages);
        }

        [Fact]
        public async Task Keep_TimerFileThereButNotEnabled_IsLeftAsItIs()
        {
            _timer.CurrentInterval = "2h";
            _timer.IsEnabled = false;

            (int exitCode, MockOutputWriter output) = await RunAsync("1h", "--keep");

            Assert.Equal(0, exitCode);
            Assert.False(_timer.IsEnabled);
            Assert.Equal(new[] { "The update timer stays as it was (2h). Turn it off with 'updaemon timer -'." }, output.Messages);
        }

        [Fact]
        public async Task Keep_TimerTurnedOff_StaysOff()
        {
            Assert.Equal(0, (await RunAsync("2h")).ExitCode);
            Assert.Equal(0, (await RunAsync("-")).ExitCode);

            (int exitCode, MockOutputWriter output) = await RunAsync("1h", "--keep");

            Assert.Equal(0, exitCode);
            Assert.False(_timer.IsEnabled);
            Assert.Equal(new[] { "The update timer stays off, as it was turned off. Turn it on with 'updaemon timer 1h'." }, output.Messages);
        }

        [Fact]
        public async Task Keep_TimerTurnedOffThenOnAgain_KeepsIt()
        {
            Assert.Equal(0, (await RunAsync("-")).ExitCode);
            Assert.Equal(0, (await RunAsync("2h")).ExitCode);

            (int exitCode, MockOutputWriter output) = await RunAsync("1h", "--keep");

            Assert.Equal(0, exitCode);
            Assert.Equal(new[] { "The update timer stays as it was (2h). Turn it off with 'updaemon timer -'." }, output.Messages);
        }

        [Fact]
        public async Task Off_RemovesTheTimerAndRecordsItInTheConfig()
        {
            Assert.Equal(0, (await RunAsync("2h")).ExitCode);

            Assert.Equal(0, (await RunAsync("-")).ExitCode);

            Assert.False(_timer.IsEnabled);
            Assert.Null(_timer.CurrentInterval);
            Assert.True((await _config.LoadConfigAsync()).TimerTurnedOff);
        }

        [Fact]
        public async Task Off_RemovingFails_IsStillRecordedOff()
        {
            _timer.RemoveFailure = new InvalidOperationException("systemctl daemon-reload failed.");

            await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync("-"));

            Assert.True((await _config.LoadConfigAsync()).TimerTurnedOff);
        }

        [Theory]
        [InlineData("-", "--keep")]
        [InlineData("--keep")]
        [InlineData("1h", "2h")]
        public async Task WrongArguments_ShowTheUsage(params string[] args)
        {
            (int exitCode, MockOutputWriter output) = await RunAsync(args);

            Assert.Equal(1, exitCode);
            Assert.Equal(new[] { "Usage: updaemon timer [<interval> [--keep] | -]" }, output.Errors);
            Assert.Empty(_timer.MethodCalls);
        }

        [Fact]
        public async Task UnknownOption_IsNamed()
        {
            (int exitCode, MockOutputWriter output) = await RunAsync("1h", "--other");

            Assert.Equal(1, exitCode);
            Assert.Equal(new[] { "Error: Unknown option '--other'." }, output.Errors);
            Assert.Empty(_timer.MethodCalls);
        }
    }
}
