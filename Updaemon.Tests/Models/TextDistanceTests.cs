using Updaemon.Models;

namespace Updaemon.Tests.Models
{
    public class TextDistanceTests
    {
        [Theory]
        [InlineData("install", "install", 0)]
        [InlineData("instal", "install", 1)]
        [InlineData("upadte", "update", 2)]
        [InlineData("remove", "status", 6)]
        [InlineData("", "set", 3)]
        public void Between_CountsSingleCharacterEdits(string a, string b, int expected)
        {
            Assert.Equal(expected, TextDistance.Between(a, b));
        }
    }
}
