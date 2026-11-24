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
            Assert.Equal("root", root.Name);
            Assert.Equal(0u, root.Id);
            Assert.Equal(OperatingSystem.IsMacOS() ? "/var/root" : "/root", root.Home);
        }

        [Fact]
        public void FindUser_UserId_IsThatUser()
        {
            Assert.Equal(LibC.FindUser("root"), LibC.FindUser("0"));
        }

        [Theory]
        [InlineData("no-such-user-updaemon")]
        [InlineData("4294967290")]
        [InlineData("+0")]
        [InlineData("")]
        public void FindUser_NoSuchUser_IsNull(string nameOrId)
        {
            Assert.Null(LibC.FindUser(nameOrId));
        }
    }
}
