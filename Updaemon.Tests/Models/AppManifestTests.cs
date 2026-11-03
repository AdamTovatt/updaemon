using Updaemon.Models;

namespace Updaemon.Tests.Models
{
    public class AppManifestTests
    {
        // Like AppHarness.ByteShelfManifest, but LOG_LEVEL is also marked required: a setting with a
        // default must still never count as missing.
        private const string ByteShelfManifest = """
            {
              "executable": "bin/byteshelf",
              "command": "byteshelf",
              "service": { "args": ["sync", "run"] },
              "settings": {
                "BYTESHELF_URL": { "description": "Server URL", "required": true },
                "BYTESHELF_API_KEY": { "description": "API key", "required": true, "secret": true },
                "BYTESHELF_LOG_LEVEL": { "description": "Log level", "default": "info", "required": true }
              }
            }
            """;

        [Fact]
        public void Parse_FullManifest_ReadsEveryField()
        {
            AppManifest manifest = AppManifest.Parse(ByteShelfManifest);

            Assert.Equal("bin/byteshelf", manifest.Executable);
            Assert.Equal("byteshelf", manifest.Command);
            Assert.Equal(new[] { "sync", "run" }, manifest.Service!.Args);
            Assert.Equal("command + service", manifest.KindLabel);
            Assert.True(manifest.IsSecret("BYTESHELF_API_KEY"));
            Assert.False(manifest.IsSecret("BYTESHELF_URL"));
        }

        [Fact]
        public void MissingRequiredSettings_SkipsPresentKeysAndSettingsWithDefaults()
        {
            AppManifest manifest = AppManifest.Parse(ByteShelfManifest);

            Assert.Equal(new[] { "BYTESHELF_API_KEY", "BYTESHELF_URL" }, manifest.MissingRequiredSettings(Array.Empty<string>()));
            Assert.Equal(new[] { "BYTESHELF_API_KEY" }, manifest.MissingRequiredSettings(new[] { "BYTESHELF_URL" }));
        }

        [Fact]
        public void Parse_ServiceWithoutArgs_DefaultsToNone()
        {
            AppManifest manifest = AppManifest.Parse("""{ "executable": "app", "service": {} }""");

            Assert.Empty(manifest.Service!.Args);
            Assert.Equal("service", manifest.KindLabel);
        }

        [Fact]
        public void Parse_ExplicitNulls_ReadAsAbsent()
        {
            AppManifest manifest = AppManifest.Parse("""{ "executable": "app", "service": { "args": null }, "settings": null }""");

            Assert.Empty(manifest.Settings);
            Assert.Empty(manifest.Service!.Args);
        }

        [Theory]
        [InlineData("""{ "executablePath": "bin/app" }""", "old format")]
        [InlineData("""{ "command": "app" }""", "executable")]
        [InlineData("""{ "executable": "app" }""", "neither 'command' nor 'service'")]
        [InlineData("""{ "executable": "../app", "command": "app" }""", "inside the release")]
        [InlineData("""{ "executable": "/usr/bin/app", "command": "app" }""", "inside the release")]
        [InlineData("""{ "executable": "app", "command": "a/b" }""", "'command'")]
        [InlineData("""{ "executable": "app", "command": "app", "settings": { "BAD-KEY": {} } }""", "BAD-KEY")]
        [InlineData("""{ "executable": "app", "command": "app", "settings": { "UPDAEMON_DATA_DIR": {} } }""", "reserved")]
        [InlineData("""{ "executable": "app", "command": "app", "settings": { "A": { "required": "yes" } } }""", "required")]
        [InlineData("not json", "Invalid updaemon.json")]
        [InlineData("""{ "executable": "app\nUser=root", "command": "app" }""", "inside the release")]
        [InlineData("""{ "executable": "app", "service": { "args": ["run\nUser=root"] } }""", "control character")]
        [InlineData("""{ "executable": "app", "command": "app", "settings": { "A": { "default": "x\ny" } } }""", "default of 'A'")]
        public void Parse_InvalidManifest_ThrowsNamingTheProblem(string json, string expectedInMessage)
        {
            ManifestException ex = Assert.Throws<ManifestException>(() => AppManifest.Parse(json));

            Assert.StartsWith("Invalid updaemon.json", ex.Message);
            Assert.Contains(expectedInMessage, ex.Message);
        }
    }
}
