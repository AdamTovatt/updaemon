using Updaemon.Interfaces;
using Updaemon.Models;

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

        /// <summary>Services <see cref="EnableServiceAsync"/> enabled and nothing disabled since.</summary>
        public HashSet<string> EnabledServices { get; } = new HashSet<string>();
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
            ServiceRunningStates[serviceName] = !Crashes(serviceName);
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
            ServiceRunningStates[serviceName] = !Crashes(serviceName);
            return Task.CompletedTask;
        }

        public Task EnableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(EnableServiceAsync), serviceName));
            EnabledServices.Add(serviceName);
            return Task.CompletedTask;
        }

        public Task DisableServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(DisableServiceAsync), serviceName));
            EnabledServices.Remove(serviceName);
            return Task.CompletedTask;
        }

        public Task<bool> IsEnabledAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(EnabledServices.Contains(serviceName));
        }

        public Task DaemonReloadAsync(CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(DaemonReloadAsync)));
            return Task.CompletedTask;
        }

        /// <summary>What <see cref="ServicesStopAtLogoutAsync"/> answers; false, as when lingering is on.</summary>
        public bool ServicesStopAtLogout { get; set; }

        public Task<bool> ServicesStopAtLogoutAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ServicesStopAtLogout);
        }

        /// <summary>Set to make <see cref="EnsureReachableAsync"/> fail with this message, as without a user manager.</summary>
        public string? Unreachable { get; set; }

        /// <summary>How often <see cref="EnsureReachableAsync"/> was asked; kept apart from <see cref="MethodCalls"/>, which records what changes services.</summary>
        public int ReachChecks { get; private set; }

        public Task<ReachableServiceManager> EnsureReachableAsync(CancellationToken cancellationToken = default)
        {
            ReachChecks++;
            return Unreachable == null ? Task.FromResult(new ReachableServiceManager()) : throw new ServiceManagerUnreachableException(Unreachable);
        }

        /// <summary>
        /// Services whose next start or restart crashes: not running afterwards, so the health check
        /// fails. A later start runs again, as the version rolled back to does.
        /// </summary>
        public HashSet<string> CrashOnNextStart { get; } = new HashSet<string>();

        /// <summary>Called when a start or restart crashes: to break something just before the rollback that follows.</summary>
        public Action? OnCrash { get; set; }

        private bool Crashes(string serviceName)
        {
            if (!CrashOnNextStart.Remove(serviceName))
            {
                return false;
            }

            OnCrash?.Invoke();
            return true;
        }

        /// <summary>Services the service manager keeps restarting: active, but started again at every look.</summary>
        public HashSet<string> RestartingServices { get; } = new HashSet<string>();

        /// <summary>
        /// Whether the service runs at each next reading, used up before the states above apply: a
        /// service that comes up late, or crashes during the health check's wait.
        /// </summary>
        public Dictionary<string, Queue<bool>> ScriptedReadings { get; } = new Dictionary<string, Queue<bool>>();

        private int _starts;

        public Task<ServiceState> GetStateAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(GetStateAsync), serviceName));
            bool active = ScriptedReadings.TryGetValue(serviceName, out Queue<bool>? readings) && readings.TryDequeue(out bool scripted)
                ? scripted
                : ServiceRunningStates.GetValueOrDefault(serviceName, false);
            int starts = RestartingServices.Contains(serviceName) ? ++_starts : 0;
            return Task.FromResult(new ServiceState(active, starts));
        }

        public Task<int> ShowLogsAsync(string serviceName, bool follow, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(ShowLogsAsync), $"{serviceName}:{follow}"));
            return Task.FromResult(0);
        }

        public Task<bool> ServiceExistsAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add(Call(nameof(ServiceExistsAsync), serviceName));
            return Task.FromResult(ExistingServices.Contains(serviceName));
        }
    }
}
