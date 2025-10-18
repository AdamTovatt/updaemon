using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of ITimerManager with recorded method calls.
    /// </summary>
    public class MockTimerManager : ITimerManager
    {
        public List<string> MethodCalls { get; } = new List<string>();
        public bool IsEnabled { get; set; } = false;
        public string? CurrentInterval { get; set; } = null;
        public TimeSpan? LastSetInterval { get; set; } = null;

        public Task SetTimerAsync(TimeSpan interval, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"{nameof(SetTimerAsync)}:{interval}");
            LastSetInterval = interval;
            IsEnabled = true;
            CurrentInterval = ConvertToDisplayFormat(interval);
            return Task.CompletedTask;
        }

        public Task DisableTimerAsync(CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(nameof(DisableTimerAsync));
            IsEnabled = false;
            CurrentInterval = null;
            return Task.CompletedTask;
        }

        public Task<bool> IsTimerEnabledAsync(CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(nameof(IsTimerEnabledAsync));
            return Task.FromResult(IsEnabled);
        }

        public Task<string?> GetCurrentIntervalAsync(CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(nameof(GetCurrentIntervalAsync));
            return Task.FromResult(CurrentInterval);
        }

        private static string ConvertToDisplayFormat(TimeSpan interval)
        {
            if (interval.TotalMinutes < 1)
            {
                return $"{(int)interval.TotalSeconds}s";
            }
            else if (interval.TotalHours < 1)
            {
                return $"{(int)interval.TotalMinutes}m";
            }
            else
            {
                return $"{(int)interval.TotalHours}h";
            }
        }
    }
}