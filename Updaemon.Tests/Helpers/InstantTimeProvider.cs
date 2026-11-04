namespace Updaemon.Tests.Helpers
{
    /// <summary>
    /// Time that passes only by waiting: every delay completes at once and moves the clock on by
    /// its length, so a health check runs without waiting and tests can read what it waited.
    /// </summary>
    public sealed class InstantTimeProvider : TimeProvider
    {
        private readonly object _lock = new object();
        private readonly List<TimeSpan> _waits = new List<TimeSpan>();
        private DateTimeOffset _now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>Each delay asked for, in order.</summary>
        public IReadOnlyList<TimeSpan> Waits
        {
            get
            {
                lock (_lock)
                {
                    return _waits.ToArray();
                }
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock)
            {
                return _now;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_lock)
            {
                _now += dueTime;
                _waits.Add(dueTime);
            }

            // Fired after this returns, as a real timer would.
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new FiredTimer();
        }

        private sealed class FiredTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
