using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Commands
{
    public class RemoveCommandTests
    {
        [Fact]
        public async Task Remove_DeletesTheAppAndItsConfigEntry_KeepsItsData()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallByteShelfAsync();

                int exitCode = await h.Remove().ExecuteAsync(new[] { "byteshelf" });

                Assert.Equal(0, exitCode);
                Assert.Null(await h.Config.GetAppAsync("byteshelf"));
                Assert.False(Directory.Exists(h.Scope.GetAppDirectory("byteshelf")));
                Assert.True(Directory.Exists(h.Scope.GetDataDirectory("byteshelf")));
                Assert.Contains($"Removed byteshelf. Its data is kept in {h.Scope.GetDataDirectory("byteshelf")}.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task Remove_NotInstalled_Fails()
        {
            using (AppHarness h = new AppHarness())
            {
                int exitCode = await h.Remove().ExecuteAsync(new[] { "missing" });

                Assert.Equal(1, exitCode);
                Assert.Contains("Error: 'missing' is not installed.", h.Output.Errors);
            }
        }
    }
}
