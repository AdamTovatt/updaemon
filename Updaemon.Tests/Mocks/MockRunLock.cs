using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Records whether the lock is held.
    /// </summary>
    public class MockRunLock : IRunLock
    {
        public int Acquisitions { get; private set; }

        public bool IsHeld { get; private set; }

        public Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            Acquisitions++;
            IsHeld = true;
            return Task.FromResult<IDisposable>(new Release(this));
        }

        private sealed class Release : IDisposable
        {
            private readonly MockRunLock _owner;

            public Release(MockRunLock owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                _owner.IsHeld = false;
            }
        }
    }
}
