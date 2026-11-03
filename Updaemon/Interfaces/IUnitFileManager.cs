using Updaemon.Models;

namespace Updaemon.Interfaces
{
    /// <summary>
    /// Writes the systemd unit or launchd plist that runs an app as a service.
    /// </summary>
    public interface IUnitFileManager
    {
        /// <summary>Generates the unit for <paramref name="unit"/> and writes it to the scope's unit path, replacing any existing one.</summary>
        Task WriteUnitFileAsync(ServiceUnit unit, CancellationToken cancellationToken = default);
    }
}
