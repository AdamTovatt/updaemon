using Updaemon.IO;

namespace Updaemon.Tests.IO
{
    public class LibCTests
    {
        [Fact]
        public void FindUser_Root_IsUserZeroWithItsHome()
        {
            // Every Linux and macOS user database has root; its home is /root on Linux and /var/root on macOS.
            LibC.User? root = LibC.FindUser("root");

            Assert.NotNull(root);
            Assert.Equal(0u, root.Id);
            Assert.Equal(OperatingSystem.IsMacOS() ? "/var/root" : "/root", root.Home);
        }

        [Fact]
        public void FindUser_NoSuchUser_IsNull()
        {
            Assert.Null(LibC.FindUser("no-such-user-updaemon"));
        }
    }
}
