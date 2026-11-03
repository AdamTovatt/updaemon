using Updaemon.Configuration;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    public class ScopeLockTests
    {
        [Fact]
        public async Task AcquireAsync_WhileHeld_WaitsAndSaysSo_ThenProceedsOnRelease()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory);
                MockOutputWriter outputWriter = new MockOutputWriter();
                ScopeLock scopeLock = new ScopeLock(scope, outputWriter, TimeSpan.FromMilliseconds(20));

                IDisposable first = await scopeLock.AcquireAsync();
                Task<IDisposable> second = scopeLock.AcquireAsync();
                await Task.Delay(200);

                Assert.False(second.IsCompleted);
                Assert.Contains(outputWriter.Messages, m => m.Contains("Another updaemon run is in progress"));

                first.Dispose();
                Task finished = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(5)));
                Assert.Same(second, finished);
                (await second).Dispose();
            }
        }

        [Fact]
        public async Task AcquireAsync_UnwritableScopeDirectory_FailsNamingIt()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                UpdaemonScope scope = TestScope.Create(tempHelper.TempDirectory);
                // A file where the directory should be: unwritable for every user, root included,
                // where a read-only directory would not stop root.
                File.WriteAllText(scope.BinDirectory, "");
                ScopeLock scopeLock = new ScopeLock(scope, new MockOutputWriter());

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => scopeLock.AcquireAsync());

                Assert.Contains(scope.BinDirectory, ex.Message);
            }
        }
    }
}
