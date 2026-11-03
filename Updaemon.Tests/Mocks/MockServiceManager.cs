using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IServiceManager with recorded method calls, each as
    /// <see cref="Call"/> formats it.
    /// </summary>
    public class MockServiceManager : IServiceManager
    {
        public List<string> MethodCalls { get; } = new List<string>();
        public Dictionary<string, bool> ServiceRunningStates { get; } = new Dictionary<string, bool>();
        public HashSet<string> ExistingServices { get; } = new HashSet<string>();
        /// <summary>Thrown by <see cref="StartServiceAsync"/> when set.</summary>
        public Exception? StartFailure { get; set; }

        /// <summary>Thrown by <see cref="RestartServiceAsync"/> when set.</summary>
        public Exception? RestartFailure { get; set; }

        /// <summary>How a call is recorded in <see cref="MethodCalls"/>; pass the method with <c>nameof</c>.</summary>
        public static string Call(string method, string? serviceName = null) =>
            serviceName == null ? method : $"{method}:{serviceName}";

        public Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(StartServiceAsync), serviceName));
            if (StartFailure != null)
                throw StartFailure;
            ServiceRunningStates[serviceName] = true;
            return Task.CompletedTask;
        }

        public Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(StopServiceAsync), serviceName));
            ServiceRunningStates[serviceName] = false;
            return Task.CompletedTask;
        }

        public Task RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(RestartServiceAsync), serviceName));
            if (RestartFailure != null)
                throw RestartFailure;
            ServiceRunningStates[serviceName] = true;
            return Task.CompletedTask;
        }

        public Task EnableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(EnableServiceAsync), serviceName));
            return Task.CompletedTask;
        }

        public Task DisableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(DisableServiceAsync), serviceName));
            return Task.CompletedTask;
        }

        public Task<bool> IsServiceRunningAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(IsServiceRunningAsync), serviceName));
            return Task.FromResult(ServiceRunningStates.GetValueOrDefault(serviceName, false));
        }

        public Task DaemonReloadAsync(CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(DaemonReloadAsync)));
            return Task.CompletedTask;
        }

        public Task<bool> ServiceExistsAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(ServiceExistsAsync), serviceName));
            return Task.FromResult(ExistingServices.Contains(serviceName));
        }
    }
}
