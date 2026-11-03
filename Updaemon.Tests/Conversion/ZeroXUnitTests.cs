using Updaemon.Conversion;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Conversion
{
    public class ZeroXUnitTests
    {
        private const string Current = "/opt/my-api/current";

        private static string SystemdUnit(string serviceLines = "") => ZeroXLayout.Unit("my-api", Current, "MyApi", serviceLines);

        private const string PlistProgram = $"""
            <key>ProgramArguments</key>
            <array>
                <string>{Current}/MyApi</string>
            </array>
            """;

        private static string Plist(string extraEntries = "", string program = PlistProgram) => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>com.updaemon.my-api</string>
                <key>WorkingDirectory</key>
                <string>{Current}</string>
                {program}
                {extraEntries}
                <key>RunAtLoad</key>
                <true/>
                <key>KeepAlive</key>
                <true/>
                <key>ThrottleInterval</key>
                <integer>10</integer>
                <key>StandardOutPath</key>
                <string>/var/log/updaemon/my-api.log</string>
                <key>StandardErrorPath</key>
                <string>/var/log/updaemon/my-api.err.log</string>
            </dict>
            </plist>
            """;

        private static ZeroXUnitReading Read(string text, bool isMacOS = false) => ZeroXUnit.Read(text, "my-api", Current, isMacOS);

        private static ZeroXUnitReading.AsWritten AsWritten(string text, bool isMacOS = false) => Assert.IsType<ZeroXUnitReading.AsWritten>(Read(text, isMacOS));

        [Fact]
        public void Read_UnitAsZeroXWroteIt_ReadsTheExecutableAndNoSettings()
        {
            ZeroXUnitReading.AsWritten reading = AsWritten(SystemdUnit());

            Assert.Equal("MyApi", reading.ExecutableName);
            Assert.Empty(reading.Settings);
        }

        [Theory]
        [InlineData("Environment=A=1", "A=1")]
        [InlineData("Environment=A=1 B=two", "A=1;B=two")]
        [InlineData("Environment=\"A=with space\" \"B=say \\\"hi\\\"\"", "A=with space;B=say \"hi\"")]
        [InlineData("Environment=A=1\nEnvironment=A=2", "A=2")]
        [InlineData("# a comment\nEnvironment=A=$HOME", "A=$HOME")]
        public void Read_EnvironmentLines_BecomeSettings(string lines, string expected)
        {
            ZeroXUnitReading.AsWritten reading = AsWritten(SystemdUnit(lines));

            Assert.Equal(expected, string.Join(";", reading.Settings.OrderBy(s => s.Key).Select(s => $"{s.Key}={s.Value}")));
        }

        [Theory]
        [InlineData("Environment=A=%n", "the line 'Environment=A=%n' is not one updaemon can read as settings")]
        [InlineData("Environment=", "the line 'Environment=' is not one updaemon can read as settings")]
        [InlineData("Environment=1A=x", "the line 'Environment=1A=x' is not one updaemon can read as settings")]
        [InlineData("Environment=UPDAEMON_DATA_DIR=/x", "the line 'Environment=UPDAEMON_DATA_DIR=/x' is not one updaemon can read as settings")]
        [InlineData("Environment=\"A=1", "the line 'Environment=\"A=1' is not one updaemon can read as settings")]
        [InlineData("User=api", "the line 'User=api' differs from what updaemon 0.x wrote")]
        [InlineData("EnvironmentFile=/etc/my-api.env", "the line 'EnvironmentFile=/etc/my-api.env' differs from what updaemon 0.x wrote")]
        public void Read_AnyOtherEdit_IsRefusedNamingTheLine(string lines, string expected)
        {
            Assert.Equal(new ZeroXUnitReading.HandEdited(expected), Read(SystemdUnit(lines)));
        }

        [Fact]
        public void Read_ExecStartChanged_IsRefused()
        {
            Assert.Equal(
                new ZeroXUnitReading.HandEdited($"it has no 'ExecStart={Current}/<executable>' line"),
                Read(SystemdUnit().Replace($"ExecStart={Current}/MyApi", "ExecStart=/usr/bin/other")));
        }

        [Fact]
        public void Read_LineRemoved_IsRefused()
        {
            Assert.Equal(new ZeroXUnitReading.HandEdited("the line 'RestartSec=10' differs from what updaemon 0.x wrote"), Read(SystemdUnit().Replace("Restart=always\n", string.Empty)));
        }

        [Fact]
        public void Read_LastLineRemoved_NamesTheMissingLine()
        {
            Assert.Equal(new ZeroXUnitReading.HandEdited("the line 'WantedBy=multi-user.target' is missing"), Read(SystemdUnit().Replace("WantedBy=multi-user.target\n", string.Empty)));
        }

        [Fact]
        public void Read_LineAddedAfterTheLast_IsRefusedNamingIt()
        {
            Assert.Equal(new ZeroXUnitReading.HandEdited("the line 'Alias=api.service' differs from what updaemon 0.x wrote"), Read(SystemdUnit() + "Alias=api.service\n"));
        }

        [Fact]
        public void Read_PlistAsZeroXWroteItWithEnvironmentVariables_ReadsThem()
        {
            string environment = """
                <key>EnvironmentVariables</key>
                <dict>
                    <key>API_URL</key>
                    <string>https://api &amp; more</string>
                </dict>
                """;

            ZeroXUnitReading.AsWritten reading = AsWritten(Plist(environment), isMacOS: true);

            Assert.Equal("MyApi", reading.ExecutableName);
            Assert.Equal(new Dictionary<string, string> { ["API_URL"] = "https://api & more" }, reading.Settings);
        }

        [Theory]
        [InlineData("<key>UserName</key><string>api</string>", PlistProgram, "its 'UserName' differs from what updaemon 0.x wrote")]
        [InlineData("", "<key>ProgramArguments</key><array><string>/usr/bin/other</string></array>", $"its ProgramArguments do not run '{Current}/<executable>'")]
        [InlineData("", "", $"its ProgramArguments do not run '{Current}/<executable>'")]
        [InlineData("<key>EnvironmentVariables</key><array><string>A=1</string></array>", PlistProgram, "its EnvironmentVariables are not all names with text values")]
        [InlineData("<key>EnvironmentVariables</key><dict><key>A</key><integer>1</integer></dict>", PlistProgram, "its EnvironmentVariables are not all names with text values")]
        public void Read_PlistEditedByHand_IsRefusedNamingWhat(string extraEntries, string program, string expected)
        {
            Assert.Equal(new ZeroXUnitReading.HandEdited(expected), Read(Plist(extraEntries, program), isMacOS: true));
        }

        [Fact]
        public void Read_PlistWithAKeyRemoved_NamesIt()
        {
            string plist = Plist().Replace("<key>RunAtLoad</key>\n    <true/>\n", string.Empty);

            Assert.Equal(new ZeroXUnitReading.HandEdited("its 'RunAtLoad' is missing"), Read(plist, isMacOS: true));
        }

        [Theory]
        [InlineData("<plist><dict><key>Label</key>")]
        [InlineData("<plist><array/></plist>")]
        public void Read_TextThatIsNoPlist_IsRefused(string text)
        {
            Assert.Equal(new ZeroXUnitReading.HandEdited("it is not a plist updaemon can read"), Read(text, isMacOS: true));
        }
    }
}
