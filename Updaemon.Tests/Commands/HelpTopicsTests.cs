using Updaemon.Commands;

namespace Updaemon.Tests.Commands
{
    public class HelpTopicsTests
    {
        [Theory]
        [InlineData("[<-- Back](README.md)\n\n# Guide\n\ntext\n", "# Guide\n\ntext\n")]
        [InlineData("[<-- Back](README.md)\r\n\r\n# Guide\r\ntext\r\n", "# Guide\r\ntext\r\n")]
        [InlineData("# Guide\n\n[a link](README.md)\n", "# Guide\n\n[a link](README.md)\n")]
        [InlineData("[Other](OTHER.md)\n# Guide\n", "[Other](OTHER.md)\n# Guide\n")]
        public void WithoutBackLink_DropsOnlyAnOpeningLinkToTheReadme(string guide, string printed)
        {
            Assert.Equal(printed, HelpTopics.WithoutBackLink(guide));
        }
    }
}
