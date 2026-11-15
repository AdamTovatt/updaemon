using Updaemon.Conversion;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Conversion
{
    public class ZeroXDropInTests
    {
        private const string Path = "/units/my-api.service.d/override.conf";

        private static ZeroXDropIn Read(string text) => Assert.IsType<ZeroXDropInReading.Read>(ZeroXDropIn.Read(Path, text)).DropIn;

        [Fact]
        public void Read_UserAndEnvironment_ShowsTheUserAndOnlyTheVariableNames()
        {
            ZeroXDropIn dropIn = Read("""
                [Service]
                # run as pi
                User=pi
                Environment=TOKEN=secret-value "DB_URL=postgres://u:p@db"
                Environment=OTHER=1
                Restart=on-failure
                """);

            Assert.Equal(new[] { "User=pi", "the variables TOKEN, DB_URL", "the variable OTHER", "Restart" }, dropIn.Lines);
            Assert.Equal(new[] { "TOKEN", "DB_URL", "OTHER" }, dropIn.Variables);
            Assert.Equal(new ZeroXDropIn.UserLine("pi"), dropIn.User);
            Assert.DoesNotContain(dropIn.Lines, line => line.Contains("secret-value") || line.Contains("postgres"));
        }

        [Theory]
        [InlineData("[Service]\nUser=api\nUser=pi", "pi")]
        [InlineData("[Service]\nUser=pi\nUser=", null)]
        public void Read_SeveralUserLines_TheLastWins(string text, string? expected)
        {
            Assert.Equal(new ZeroXDropIn.UserLine(expected), Read(text).User);
        }

        [Fact]
        public void Read_UserOutsideService_IsNoUser()
        {
            ZeroXDropIn dropIn = Read("[Unit]\nUser=pi\n");

            Assert.Null(dropIn.User);
            Assert.Equal(new[] { "User" }, dropIn.Lines);
        }

        [Fact]
        public void Read_Empty_SetsNothing()
        {
            ZeroXDropIn dropIn = Read("# nothing\n");

            Assert.Empty(dropIn.Lines);
            Assert.Null(dropIn.User);
        }

        [Theory]
        [InlineData("[Service]\nUser=%i", "has the line 'User=%i', which is not one updaemon can read as the user it runs as")]
        [InlineData("[Service]\nEnvironment=\"A=1", "has an Environment= line updaemon cannot read")]
        [InlineData("[Service]\nEnvironment=", "has an Environment= line updaemon cannot read")]
        [InlineData("[Service]\nEnvironment=novalue", "has an Environment= line updaemon cannot read")]
        [InlineData("[Service]\nEnvironment=UPDAEMON_DATA_DIR=/x", "sets UPDAEMON_DATA_DIR, which updaemon sets for every app")]
        public void Read_WhatCannotBeCarriedOver_IsRefused(string text, string expected)
        {
            Assert.Equal(new ZeroXDropInReading.Unreadable(expected), ZeroXDropIn.Read(Path, text));
        }

        [Fact]
        public void FindPaths_ConfFilesInTheUnitsDirectory_InFileNameOrder()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                string unitPath = System.IO.Path.Combine(temp.CreateTempDirectory("units"), "my-api.service");
                string directory = ZeroXDropIn.DirectoryOf(unitPath);
                Directory.CreateDirectory(directory);
                foreach (string name in new[] { "override.conf", "10-run-as-pi.conf", "notes.txt", "a.conf.bak" })
                {
                    File.WriteAllText(System.IO.Path.Combine(directory, name), string.Empty);
                }

                Assert.Equal(unitPath + ".d", directory);
                Assert.Equal(new[] { "10-run-as-pi.conf", "override.conf" }, ZeroXDropIn.FindPaths(unitPath).Select(System.IO.Path.GetFileName));
            }
        }

        [Fact]
        public void FindPaths_NoDropInDirectory_IsEmpty()
        {
            Assert.Empty(ZeroXDropIn.FindPaths("/nonexistent/my-api.service"));
        }
    }
}
