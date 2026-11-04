namespace Updaemon.Models
{
    /// <summary>
    /// What the service manager reports about a service at one moment.
    /// </summary>
    /// <param name="IsActive">Whether it is running now.</param>
    /// <param name="Starts">How many times the service manager has (re)started it: systemd's NRestarts,
    /// launchd's runs. Only a change in it means something.</param>
    public sealed record ServiceState(bool IsActive, int Starts);
}
