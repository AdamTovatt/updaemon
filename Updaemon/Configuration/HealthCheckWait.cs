namespace Updaemon.Configuration
{
    /// <summary>
    /// How long a service must keep running after a restart to count as healthy.
    /// </summary>
    public sealed record HealthCheckWait(TimeSpan Value)
    {
        public static HealthCheckWait Default { get; } = new HealthCheckWait(TimeSpan.FromSeconds(10));
    }
}
