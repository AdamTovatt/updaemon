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
        /// Checks if a managed service is currently running.
        /// </summary>
        Task<bool> IsServiceRunningAsync(string serviceName, CancellationToken cancellationToken = default);

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
    }
}

