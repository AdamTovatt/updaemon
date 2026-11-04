using Updaemon.Conversion;
using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of ITimerManager with recorded method calls.
    /// </summary>
    public class MockTimerManager : ITimerManager
    {
        public List<string> MethodCalls { get; } = new List<string>();
        public bool IsEnabled { get; set; } = false;
        public TimeSpan? CurrentInterval { get; set; } = null;
        public TimeSpan? LastSetInterval { get; set; } = null;

        public Task SetTimerAsync(TimeSpan interval, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(SetTimerAsync)}:{interval}");
            if (SetFailure != null)
            {
                throw SetFailure;
            }

            LastSetInterval = interval;
            IsEnabled = true;
            CurrentInterval = interval;
            return Task.CompletedTask;
        }

        public Task RemoveTimerAsync(ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(nameof(RemoveTimerAsync));
            if (RemoveFailure != null)
            {
                throw RemoveFailure;
            }

            IsEnabled = false;
            CurrentInterval = null;
            return Task.CompletedTask;
        }

        public Task<bool> IsTimerEnabledAsync(CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(nameof(IsTimerEnabledAsync));
            return Task.FromResult(IsEnabled);
        }

        public Task<TimeSpan?> GetCurrentIntervalAsync(CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(nameof(GetCurrentIntervalAsync));
            return Task.FromResult(CurrentInterval);
        }

        /// <summary>The 0.x timer this scope has, if any; removing it clears this.</summary>
        public ZeroXTimer? ZeroX { get; set; }

        /// <summary>When set, <see cref="SetTimerAsync"/> throws it.</summary>
        public Exception? SetFailure { get; set; }

        /// <summary>Thrown by <see cref="RemoveTimerAsync"/> when set, before anything is removed.</summary>
        public Exception? RemoveFailure { get; set; }

        public Task<ZeroXTimer?> FindZeroXTimerAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ZeroX);
        }

        public Task RemoveZeroXTimerAsync(ZeroXTimer timer, ReachableServiceManager reachable, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(nameof(RemoveZeroXTimerAsync));
            ZeroX = null;
            return Task.CompletedTask;
        }
    }
}