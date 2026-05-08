using Updaemon.Configuration;

namespace Updaemon.Tests.Configuration
{
    public class PlatformPathsTests
    {
        [Fact]
        public void IsMacOS_MatchesRuntimeCheck()
        {
            Assert.Equal(OperatingSystem.IsMacOS(), PlatformPaths.IsMacOS);
        }

        [Fact]
        public void ConfigDirectory_ReturnsKnownPathPerOS()
        {
            string dir = PlatformPaths.ConfigDirectory;

            if (OperatingSystem.IsMacOS())
                Assert.Equal("/usr/local/var/updaemon", dir);
            else
                Assert.Equal("/var/lib/updaemon", dir);
        }

        [Fact]
        public void UnitFileExtension_DiffersPerOS()
        {
            string ext = PlatformPaths.UnitFileExtension;

            if (OperatingSystem.IsMacOS())
                Assert.Equal(".plist", ext);
            else
                Assert.Equal(".service", ext);
        }

        [Fact]
        public void GetUnitLabel_PrefixesOnMacOnly()
        {
            string label = PlatformPaths.GetUnitLabel("foo");

            if (OperatingSystem.IsMacOS())
                Assert.Equal("com.updaemon.foo", label);
            else
                Assert.Equal("foo", label);
        }

        [Fact]
        public void EmbeddedTemplateResourceName_RoutesToCorrectTemplate()
        {
            string resource = PlatformPaths.EmbeddedTemplateResourceName;

            if (OperatingSystem.IsMacOS())
                Assert.Equal("Updaemon.Templates.launchd.plist.template", resource);
            else
                Assert.Equal("Updaemon.Templates.service.template", resource);
        }
    }
}
