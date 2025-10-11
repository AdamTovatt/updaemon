namespace Updaemon.Interfaces
{
    /// <summary>
    /// Manages systemd services.
    /// </summary>
    public interface IServiceManager
    {
        /// <summary>
        /// Starts a systemd service.
        /// </summary>
        Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops a systemd service.
        /// </summary>
        Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Restarts a systemd service.
        /// </summary>
        Task RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables a systemd service to start on boot.
        /// </summary>
        Task EnableServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Disables a systemd service from starting on boot.
        /// </summary>
        Task DisableServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a systemd service is running.
        /// </summary>
        Task<bool> IsServiceRunningAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a systemd service exists.
        /// </summary>
        Task<bool> ServiceExistsAsync(string serviceName, CancellationToken cancellationToken = default);
    }
}

