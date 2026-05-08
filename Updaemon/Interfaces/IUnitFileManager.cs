namespace Updaemon.Interfaces
{
    /// <summary>
    /// Manages service unit file templates and generation. On Linux this produces
    /// systemd .service files; on macOS it produces launchd .plist files.
    /// </summary>
    public interface IUnitFileManager
    {
        /// <summary>
        /// Reads the raw unit file template without any substitutions.
        /// </summary>
        /// <returns>The raw template content.</returns>
        Task<string> ReadTemplateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the unit file template and substitutes placeholders with provided values.
        /// </summary>
        /// <param name="serviceName">The name of the service.</param>
        /// <param name="symlinkPath">The path to the symlink directory.</param>
        /// <param name="executableName">The name of the executable file.</param>
        /// <returns>The unit file content with substitutions applied.</returns>
        Task<string> ReadTemplateWithSubstitutionsAsync(string serviceName, string symlinkPath, string executableName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates a unit file from the template and writes it to the specified path.
        /// </summary>
        Task WriteUnitFileAsync(string unitFilePath, string serviceName, string symlinkPath, string executableName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the absolute path of the unit file for the given service, including
        /// the OS-appropriate directory and extension (e.g. /etc/systemd/system/foo.service
        /// on Linux, /Library/LaunchDaemons/com.updaemon.foo.plist on macOS).
        /// </summary>
        string GetUnitFilePath(string serviceName);

        /// <summary>
        /// Verifies that the manager can write to the unit-file directory. Throws if the
        /// directory does not exist or is not writable.
        /// </summary>
        Task EnsureWritableAsync(CancellationToken cancellationToken = default);
    }
}
