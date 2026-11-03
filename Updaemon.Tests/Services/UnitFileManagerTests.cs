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
            ExecutablePath = "/apps/byteshelf/current/bin/byteshelf",
            Arguments = new[] { "sync", "run", "50% $HOME \"q\"" },
            RunAs = runAs,
            SettingsFilePath = "/root/settings/byteshelf.env",
            SecretsFilePath = "/root/secrets/byteshelf.env",
            Environment = new Dictionary<string, string> { ["URL"] = "https://x?a=1&b=<2>", ["TOKEN"] = "s3cret" },
        };

        [Fact]
        public void Render_SystemdSystemScope_RunsAsUserWithEnvironmentFilesAndQuotedArguments()
        {
            UnitFileManager manager = new UnitFileManager(TestScope.Create("/t", isSystem: true));

            string unit = manager.Render(Unit(runAs: "shelf"));

            Assert.Contains("User=shelf\n", unit);
            Assert.Contains("WorkingDirectory=/apps/byteshelf/current\n", unit);
            Assert.Contains("EnvironmentFile=/root/settings/byteshelf.env\n", unit);
            Assert.Contains("EnvironmentFile=-/root/secrets/byteshelf.env\n", unit);
            Assert.Contains("ExecStart=\"/apps/byteshelf/current/bin/byteshelf\" \"sync\" \"run\" \"50%% $$HOME \\\"q\\\"\"\n", unit);
            Assert.Contains("WantedBy=multi-user.target", unit);
            Assert.DoesNotContain("s3cret", unit);
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
        public void Render_Launchd_CarriesArgumentsEnvironmentAndUserEscaped()
        {
            UnitFileManager manager = new UnitFileManager(TestScope.Create("/t", isSystem: true, isMacOS: true));

            string plist = manager.Render(Unit(runAs: "shelf"));

            Assert.Contains("<string>com.updaemon.byteshelf</string>", plist);
            Assert.Contains("<key>UserName</key>\n    <string>shelf</string>", plist);
            Assert.Contains("<string>/apps/byteshelf/current/bin/byteshelf</string>\n        <string>sync</string>\n        <string>run</string>", plist);
            Assert.Contains("<key>URL</key>\n        <string>https://x?a=1&amp;b=&lt;2&gt;</string>", plist);
            Assert.Contains("<key>TOKEN</key>\n        <string>s3cret</string>", plist);
            Assert.Contains("<string>/t/logs/byteshelf.log</string>", plist);
        }

        [Theory]
        [InlineData(false, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead)]
        [InlineData(true, UnixFileMode.UserRead | UnixFileMode.UserWrite)]
        public async Task WriteUnitFileAsync_PlistWithSecretsIsOwnerOnly(bool isMacOS, UnixFileMode expectedMode)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory, isMacOS: isMacOS);
                UnitFileManager manager = new UnitFileManager(scope);

                await manager.WriteUnitFileAsync(Unit());

                string path = scope.GetUnitFilePath("byteshelf");
                Assert.Equal(manager.Render(Unit()), await File.ReadAllTextAsync(path));
                Assert.Equal(expectedMode, File.GetUnixFileMode(path));
            }
        }
    }
}
