using Updaemon.Models;

namespace Updaemon.Tests.Models
{
    public class ExtensionNameTests
    {
        [Fact]
        public void TryParse_HostAndId_ReadsBoth()
        {
            Assert.Equal(new ExtensionName("shelf", "images"), ExtensionName.TryParse("shelf/images"));
        }

        [Theory]
        [InlineData("shelf")]
        [InlineData("shelf/")]
        [InlineData("/images")]
        [InlineData("shelf/images/2")]
        [InlineData("shelf/..")]
        [InlineData("shelf/updaemon")]
        [InlineData("updaemon/images")]
        public void TryParse_AnythingElse_IsNull(string name)
        {
            Assert.Null(ExtensionName.TryParse(name));
        }
    }
}
