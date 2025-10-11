namespace Updaemon.Interfaces
{
    /// <summary>
    /// Manages systemd unit file templates and generation.
    /// </summary>
    public interface IUnitFileManager
    {
        /// <summary>
        /// Reads the raw unit file template without any substitutions.
        /// </summary>
        /// <returns>The raw template content.</returns>
        Task<string> ReadTemplateAsync();

        /// <summary>
        /// Reads the unit file template and substitutes placeholders with provided values.
        /// </summary>
        /// <param name="serviceName">The name of the service.</param>
        /// <param name="executablePath">The path to the executable.</param>
        /// <returns>The unit file content with substitutions applied.</returns>
        Task<string> ReadTemplateWithSubstitutionsAsync(string serviceName, string executablePath);
    }
}

