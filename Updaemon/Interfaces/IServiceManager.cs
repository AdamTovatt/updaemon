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
        Task StartServiceAsync(string serviceName);

        /// <summary>
        /// Stops a systemd service.
        /// </summary>
        Task StopServiceAsync(string serviceName);

        /// <summary>
        /// Restarts a systemd service.
        /// </summary>
        Task RestartServiceAsync(string serviceName);

        /// <summary>
        /// Enables a systemd service to start on boot.
        /// </summary>
        Task EnableServiceAsync(string serviceName);

        /// <summary>
        /// Disables a systemd service from starting on boot.
        /// </summary>
        Task DisableServiceAsync(string serviceName);

        /// <summary>
        /// Checks if a systemd service is running.
        /// </summary>
        Task<bool> IsServiceRunningAsync(string serviceName);

        /// <summary>
        /// Checks if a systemd service exists.
        /// </summary>
        Task<bool> ServiceExistsAsync(string serviceName);
    }
}

