using Updaemon.Models;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Abstracts the local service manager (systemd on Linux, launchd on macOS).
    /// </summary>
    public interface IServiceManager
    {
        /// <summary>
        /// Starts a managed service.
        /// </summary>
        Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops a managed service. On systemd the unit stays enabled. On launchd the job is unloaded,
        /// because with KeepAlive a killed process would be relaunched; <see cref="StartServiceAsync"/>
        /// loads it again.
        /// </summary>
        Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Restarts a managed service.
        /// </summary>
        Task RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables a service to start at boot. On systemd this calls "systemctl enable"; on launchd
        /// it does nothing, since launchd loads every plist in the scope's directory.
        /// </summary>
        Task EnableServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Disables a service from starting at boot.
        /// </summary>
        Task DisableServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Whether the service is running, and how often the service manager has started it. A service
        /// that does not exist is not running.
        /// </summary>
        Task<ServiceState> GetStateAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Shows the service's log on the terminal (journalctl on Linux, its log files on macOS);
        /// with <paramref name="follow"/>, keeps showing new lines until Ctrl+C. Returns the exit code.
        /// </summary>
        Task<int> ShowLogsAsync(string serviceName, bool follow, CancellationToken cancellationToken = default);

        /// <summary>
        /// Whether a service by this name exists anywhere the service manager looks, not only in this
        /// scope's unit directory: a distro's unit (for example ssh.service), or one left by updaemon 0.x.
        /// </summary>
        Task<bool> ServiceExistsAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reloads the service manager's configuration. On systemd this is "systemctl daemon-reload";
        /// on launchd it does nothing, since every start and restart reloads the plist.
        /// </summary>
        Task DaemonReloadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Whether this scope's services stop when its user logs out. Only in a Linux user scope can
        /// they: its services run in the user's own systemd manager, which stops at logout unless
        /// lingering is on for the user.
        /// </summary>
        Task<bool> ServicesStopAtLogoutAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks that this scope's service manager can be reached, before anything that needs it is
        /// written. Always true of the system's; a user's runs only while the user is logged in or
        /// lingering is on (Linux), or while the user is logged in to the Mac's desktop.
        /// </summary>
        /// <exception cref="ServiceManagerUnreachableException">It cannot; the message says why and how to fix it.</exception>
        Task<ReachableServiceManager> EnsureReachableAsync(CancellationToken cancellationToken = default);
    }
}

