using Updaemon.Commands;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Commands
{
    public class MigrateCommandTests
    {
        [Theory]
        [InlineData(new string[0], false)]
        [InlineData(new[] { "--dry-run" }, true)]
        public async Task Migrate_SystemScope_Converts(string[] args, bool dryRun)
        {
            MockZeroXConverter converter = new MockZeroXConverter();

            int exitCode = await new MigrateCommand(converter, new MockOutputWriter(), TestScope.Create("/nonexistent", isSystem: true)).ExecuteAsync(args);

            Assert.Equal(0, exitCode);
            Assert.Equal(new[] { dryRun }, converter.Runs);
        }

        [Fact]
        public async Task Migrate_NotEverythingConverted_ExitsWithOne()
        {
            MockZeroXConverter converter = new MockZeroXConverter { Result = false };

            int exitCode = await new MigrateCommand(converter, new MockOutputWriter(), TestScope.Create("/nonexistent", isSystem: true)).ExecuteAsync(Array.Empty<string>());

            Assert.Equal(1, exitCode);
        }

        [Theory]
        [InlineData(new[] { "--force" }, "Error: Unknown option '--force'.")]
        [InlineData(new[] { "my-api" }, "Usage: updaemon migrate [--dry-run]")]
        public async Task Migrate_OtherArguments_AreRefusedWithoutConverting(string[] args, string expected)
        {
            MockZeroXConverter converter = new MockZeroXConverter();
            MockOutputWriter output = new MockOutputWriter();

            int exitCode = await new MigrateCommand(converter, output, TestScope.Create("/nonexistent", isSystem: true)).ExecuteAsync(args);

            Assert.Equal(1, exitCode);
            Assert.Equal(new[] { expected }, output.Errors);
            Assert.Empty(converter.Runs);
        }

        [Fact]
        public async Task Migrate_UserScope_SaysToRunItAsRoot()
        {
            MockZeroXConverter converter = new MockZeroXConverter();
            MockOutputWriter output = new MockOutputWriter();

            int exitCode = await new MigrateCommand(converter, output, TestScope.Create("/nonexistent")).ExecuteAsync(Array.Empty<string>());

            Assert.Equal(1, exitCode);
            Assert.Equal(new[] { "Error: an updaemon 0.x setup is system-wide; run 'sudo updaemon migrate'." }, output.Errors);
            Assert.Empty(converter.Runs);
        }
    }
}
