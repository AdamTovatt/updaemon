using Updaemon.Models;

namespace Updaemon.Tests.Models
{
    public class TimerIntervalTests
    {
        [Theory]
        [InlineData(30, "30s")]
        [InlineData(45, "45s")]
        [InlineData(90, "90s")]
        [InlineData(300, "5m")]
        [InlineData(3600, "1h")]
        [InlineData(7200, "2h")]
        public void Format_UsesTheLargestUnitThatNamesItExactly(int seconds, string expected)
        {
            Assert.Equal(expected, TimerInterval.Format(TimeSpan.FromSeconds(seconds)));
        }

        [Theory]
        [InlineData("30s", 30)]
        [InlineData(" 5M ", 300)]
        [InlineData("2h", 7200)]
        [InlineData("0h", null)]
        [InlineData("-1m", null)]
        [InlineData("5d", null)]
        [InlineData("h", null)]
        [InlineData("", null)]
        public void Parse_ReadsAPositiveWholeNumberOfSecondsMinutesOrHours(string interval, int? expectedSeconds)
        {
            Assert.Equal(expectedSeconds == null ? null : TimeSpan.FromSeconds(expectedSeconds.Value), TimerInterval.Parse(interval));
        }
    }
}
