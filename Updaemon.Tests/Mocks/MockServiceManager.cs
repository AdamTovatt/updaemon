using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IServiceManager with recorded method calls.
    /// </summary>
    public class MockServiceManager : IServiceManager
    {
        public List<string> MethodCalls { get; } = new List<string>();
        public Dictionary<string, bool> ServiceRunningStates { get; } = new Dictionary<string, bool>();
        public Dictionary<string, bool> ServiceExistsStates { get; } = new Dictionary<string, bool>();

        public Task StartServiceAsync(string serviceName)
        {
            MethodCalls.Add($"{nameof(StartServiceAsync)}:{serviceName}");
            ServiceRunningStates[serviceName] = true;
            return Task.CompletedTask;
        }

        public Task StopServiceAsync(string serviceName)
        {
            MethodCalls.Add($"{nameof(StopServiceAsync)}:{serviceName}");
            ServiceRunningStates[serviceName] = false;
            return Task.CompletedTask;
        }

        public Task RestartServiceAsync(string serviceName)
        {
            MethodCalls.Add($"{nameof(RestartServiceAsync)}:{serviceName}");
            ServiceRunningStates[serviceName] = true;
            return Task.CompletedTask;
        }

        public Task EnableServiceAsync(string serviceName)
        {
            MethodCalls.Add($"{nameof(EnableServiceAsync)}:{serviceName}");
            return Task.CompletedTask;
        }

        public Task DisableServiceAsync(string serviceName)
        {
            MethodCalls.Add($"{nameof(DisableServiceAsync)}:{serviceName}");
            return Task.CompletedTask;
        }

        public Task<bool> IsServiceRunningAsync(string serviceName)
        {
            MethodCalls.Add($"{nameof(IsServiceRunningAsync)}:{serviceName}");
            return Task.FromResult(ServiceRunningStates.GetValueOrDefault(serviceName, false));
        }

        public Task<bool> ServiceExistsAsync(string serviceName)
        {
            MethodCalls.Add($"{nameof(ServiceExistsAsync)}:{serviceName}");
            return Task.FromResult(ServiceExistsStates.GetValueOrDefault(serviceName, false));
        }
    }
}

