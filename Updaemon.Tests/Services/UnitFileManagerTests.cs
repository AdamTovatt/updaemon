using Updaemon.Configuration;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    public class UnitFileManagerTests
    {
        private static ServiceUnit Unit(string? runAs = null) => new ServiceUnit
        {
            AppName = "byteshelf",
            WorkingDirectory = "/apps/byteshelf/current",
            WrapperPath = "/apps/byteshelf/run",
            Arguments = new[] { "sync", "run", "50% $HOME \"q\"" },
            RunAs = runAs,
        };

        [Fact]
        public void Render_SystemdSystemScope_RunsTheWrapperAsUserWithQuotedArgumentsAndNoEnvironment()
        {
            UnitFileManager manager = new UnitFileManager(TestScope.Create("/t", isSystem: true));

            string unit = manager.Render(Unit(runAs: "shelf"));

            Assert.Contains("User=shelf\n", unit);
            Assert.Contains("WorkingDirectory=/apps/byteshelf/current\n", unit);
            Assert.Contains("ExecStart=\"/apps/byteshelf/run\" \"sync\" \"run\" \"50%% $$HOME \\\"q\\\"\"\n", unit);
            Assert.Contains("WantedBy=multi-user.target", unit);
            Assert.DoesNotContain("Environment", unit);
        }

        [Fact]
        public void Render_SystemdUserScope_TargetsDefaultTargetWithoutUser()
        {
            UnitFileManager manager = new UnitFileManager(TestScope.Create("/t"));

            string unit = manager.Render(Unit());

            Assert.DoesNotContain("User=", unit);
            Assert.DoesNotContain("network.target", unit);
            Assert.Contains("WantedBy=default.target", unit);
        }

        [Fact]
        public void Render_Launchd_RunsTheWrapperWithArgumentsAndUserAndNoEnvironment()
        {
            UnitFileManager manager = new UnitFileManager(TestScope.Create("/t", isSystem: true, isMacOS: true));

            string plist = manager.Render(Unit(runAs: "shelf") with { Arguments = new[] { "a&b" } });

            Assert.Contains("<string>com.updaemon.byteshelf</string>", plist);
            Assert.Contains("<key>UserName</key>\n    <string>shelf</string>", plist);
            Assert.Contains("<string>/apps/byteshelf/run</string>\n        <string>a&amp;b</string>\n    </array>", plist);
            Assert.DoesNotContain("EnvironmentVariables", plist);
            Assert.Contains("<string>/t/logs/byteshelf.log</string>", plist);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WriteUnitFileAsync_WritesTheRenderedFileReadableByAll(bool isMacOS)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory, isMacOS: isMacOS);
                UnitFileManager manager = new UnitFileManager(scope);

                await manager.WriteUnitFileAsync(Unit());

                string path = scope.GetUnitFilePath("byteshelf");
                Assert.Equal(manager.Render(Unit()), await File.ReadAllTextAsync(path));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, File.GetUnixFileMode(path));
            }
        }
    }
}
