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
        /// Stops a managed service. On systemd this is "systemctl stop" — the unit remains loaded
        /// and enabled. On launchd this is "launchctl bootout" — the plist is unloaded as well as
        /// the process terminated, because services with KeepAlive would otherwise be relaunched
        /// immediately. Re-starting via <see cref="StartServiceAsync"/> works on both: the launchd
        /// path re-bootstraps the plist transparently.
        /// </summary>
        Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Restarts a managed service.
        /// </summary>
        Task RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables a service to start at boot. On systemd this calls "systemctl enable";
        /// on launchd this loads/bootstraps the plist.
        /// </summary>
        Task EnableServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Disables a service from starting at boot.
        /// </summary>
        Task DisableServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a managed service is currently running.
        /// </summary>
        Task<bool> IsServiceRunningAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a unit file for the service has been written.
        /// </summary>
        Task<bool> ServiceExistsAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reloads the service manager's configuration. On systemd this is "systemctl daemon-reload";
        /// on launchd this is a no-op (launchd reloads on each bootstrap call).
        /// </summary>
        Task DaemonReloadAsync(CancellationToken cancellationToken = default);
    }
}

