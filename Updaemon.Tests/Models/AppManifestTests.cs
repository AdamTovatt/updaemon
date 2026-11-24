using Updaemon.Models;
using Updaemon.Tests.Helpers;

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
            Assert.Equal(TimeSpan.FromSeconds(10), manifest.Service.HealthCheck);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(300)]
        public void Parse_HealthCheckSecondsInRange_IsTheWindow(int seconds)
        {
            AppManifest manifest = AppManifest.Parse($$"""{ "executable": "app", "service": { "healthCheckSeconds": {{seconds}} } }""");

            Assert.Equal(TimeSpan.FromSeconds(seconds), manifest.Service!.HealthCheck);
        }

        [Fact]
        public void Parse_ExplicitNulls_ReadAsAbsent()
        {
            AppManifest manifest = AppManifest.Parse("""{ "executable": "app", "service": { "args": null }, "settings": null }""");

            Assert.Empty(manifest.Settings);
            Assert.Empty(manifest.Service!.Args);
        }

        [Fact]
        public void Parse_Extensions_ReadsEveryField()
        {
            ExtensionsSpec extensions = AppManifest.Parse(AppHarness.HostManifest).Extensions!;

            Assert.Equal("SHELF_PLUGINS", extensions.Variable);
            Assert.Equal(new[] { "--check-plugin" }, extensions.Check!);
            Assert.Equal("github:owner/shelf/images.zip", extensions.Known["images"]);
        }

        [Fact]
        public void Parse_ExtensionsWithOnlyTheVariable_HasNoCheckAndKnowsNone()
        {
            ExtensionsSpec extensions = AppManifest.Parse("""{ "executable": "app", "command": "app", "extensions": { "variable": "PLUGINS", "check": null, "known": null } }""").Extensions!;

            Assert.Null(extensions.Check);
            Assert.Empty(extensions.Known);
        }

        [Theory]
        [InlineData("""{ }""", "'extensions.variable' is missing")]
        [InlineData("""{ "variable": "" }""", "'extensions.variable' is missing")]
        [InlineData("""{ "variable": "A-B" }""", "'extensions.variable' must be an environment variable name, got 'A-B'")]
        [InlineData("""{ "variable": "UPDAEMON_DATA_DIR" }""", "'extensions.variable' cannot be UPDAEMON_DATA_DIR")]
        [InlineData("""{ "variable": "LEVEL" }""", "setting 'LEVEL' is reserved")]
        [InlineData("""{ "variable": "P", "check": ["a\nb"] }""", "'extensions.check'")]
        [InlineData("""{ "variable": "P", "check": [null] }""", "'extensions.check'")]
        [InlineData("""{ "variable": "P", "known": { "../x": "github:a/b" } }""", "'../x', which is not a valid extension id")]
        [InlineData("""{ "variable": "P", "known": { "updaemon": "github:a/b" } }""", "'updaemon', which is not a valid extension id")]
        [InlineData("""{ "variable": "P", "known": { "x": "a/b" } }""", "the source of 'x' in 'extensions.known' must be <plugin>:<remote>, got 'a/b'")]
        [InlineData("""{ "variable": "P", "known": { "x": null } }""", "the source of 'x'")]
        public void Parse_InvalidExtensions_ThrowsNamingTheProblem(string extensions, string expectedInMessage)
        {
            string json = $$"""{ "executable": "app", "command": "app", "settings": { "LEVEL": {} }, "extensions": {{extensions}} }""";

            ManifestException ex = Assert.Throws<ManifestException>(() => AppManifest.Parse(json));

            Assert.Contains(expectedInMessage, ex.Message);
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
        [InlineData("""{ "executable": "app", "service": { "healthCheckSeconds": 0 } }""", "'service.healthCheckSeconds' must be 1 to 300, got 0.")]
        [InlineData("""{ "executable": "app", "service": { "healthCheckSeconds": 301 } }""", "'service.healthCheckSeconds' must be 1 to 300, got 301.")]
        [InlineData("""{ "executable": "app", "service": { "healthCheckSeconds": "30" } }""", "Invalid updaemon.json")]
        public void Parse_InvalidManifest_ThrowsNamingTheProblem(string json, string expectedInMessage)
        {
            ManifestException ex = Assert.Throws<ManifestException>(() => AppManifest.Parse(json));

            Assert.StartsWith("Invalid updaemon.json", ex.Message);
            Assert.Contains(expectedInMessage, ex.Message);
        }
    }
}
