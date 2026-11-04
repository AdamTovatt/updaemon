namespace Updaemon.Models
{
    /// <summary>
    /// Proof that the service manager was checked before anything was written: every method that
    /// writes a unit, plist or timer, or starts or stops a service, takes one. Only
    /// <see cref="Interfaces.IServiceManager.EnsureReachableAsync"/> and
    /// <see cref="Interfaces.IServiceDeployer.ReachServiceManagerAsync"/> make one, so a command
    /// cannot write without checking first.
    /// </summary>
    public sealed class ReachableServiceManager
    {
        internal ReachableServiceManager()
        {
        }
    }
}
