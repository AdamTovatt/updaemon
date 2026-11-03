using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>A 0.x conversion that records its runs; pending until it has run once for real.</summary>
    public class MockZeroXConverter : IZeroXConverter
    {
        public bool Pending { get; set; }

        public List<bool> Runs { get; } = new List<bool>();

        /// <summary>What a run returns: whether everything was converted.</summary>
        public bool Result { get; set; } = true;

        /// <summary>When set, a run throws it.</summary>
        public Exception? Failure { get; set; }

        /// <summary>Called while a run is in progress.</summary>
        public Action? OnRun { get; set; }

        public bool IsPending() => Pending;

        public Task<bool> RunAsync(bool dryRun, CancellationToken cancellationToken = default)
        {
            Runs.Add(dryRun);
            OnRun?.Invoke();
            if (Failure != null)
            {
                throw Failure;
            }

            Pending &= dryRun;
            return Task.FromResult(Result);
        }
    }
}
