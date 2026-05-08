using System.Reflection;
using System.Security;
using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Manages service unit-file templates and generation. Produces a systemd .service
    /// file on Linux or a launchd .plist on macOS, depending on which <see cref="UnitFilePlatform"/>
    /// is selected.
    /// </summary>
    public class UnitFileManager : IUnitFileManager
    {
        private readonly string _configDirectory;
        private readonly string _templateFilePath;
        private readonly UnitFilePlatform _platform;

        /// <summary>
        /// Creates a UnitFileManager using OS-aware defaults from <see cref="PlatformPaths"/>.
        /// </summary>
        public UnitFileManager()
            : this(PlatformPaths.ConfigDirectory, UnitFilePlatform.ForCurrentOS())
        {
        }

        /// <summary>
        /// Constructs a UnitFileManager with explicit paths and template selection.
        /// </summary>
        public UnitFileManager(string configDirectory, UnitFilePlatform platform)
        {
            _configDirectory = configDirectory;
            _platform = platform;
            _templateFilePath = Path.Combine(_configDirectory, platform.TemplateFileName);
        }

        public async Task<string> ReadTemplateAsync(CancellationToken cancellationToken = default)
        {
            await EnsureTemplateExistsAsync(cancellationToken);
            return await File.ReadAllTextAsync(_templateFilePath, cancellationToken);
        }

        public async Task<string> ReadTemplateWithSubstitutionsAsync(string serviceName, string symlinkPath, string executableName, CancellationToken cancellationToken = default)
        {
            string template = await ReadTemplateAsync(cancellationToken);

            // Templates can be either INI-style (systemd) or XML (launchd plist). Escape values
            // appropriately for the format we're emitting so service names containing &, <, >, etc.
            // don't produce invalid output. SecurityElement.Escape escapes & < > " ' for XML.
            bool isXmlTemplate = template.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
            string Escape(string value) => isXmlTemplate ? (SecurityElement.Escape(value) ?? string.Empty) : value;

            string result = template
                .Replace("{SERVICE_NAME}", Escape(serviceName))
                .Replace("{DESCRIPTION}", Escape($"{serviceName} service managed by updaemon"))
                .Replace("{WORKING_DIRECTORY}", Escape(symlinkPath))
                .Replace("{EXECUTABLE_NAME}", Escape(executableName));

            return result;
        }

        public async Task WriteUnitFileAsync(string unitFilePath, string serviceName, string symlinkPath, string executableName, CancellationToken cancellationToken = default)
        {
            string content = await ReadTemplateWithSubstitutionsAsync(serviceName, symlinkPath, executableName, cancellationToken);
            await File.WriteAllTextAsync(unitFilePath, content, cancellationToken);

            // 0644 on the unit file: launchd refuses anything else; systemd is more lenient but accepts it.
            // Ownership (root:wheel on macOS) is enforced by MacServiceManager just before bootstrap;
            // keeping it out of here means UnitFileManager doesn't branch on platform.
            File.SetUnixFileMode(unitFilePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead |
                UnixFileMode.OtherRead);
        }

        public string GetUnitFilePath(string serviceName)
        {
            return Path.Combine(_platform.UnitFileDirectory, _platform.UnitLabelPrefix + serviceName + _platform.UnitFileExtension);
        }

        public Task EnsureWritableAsync(CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(_platform.UnitFileDirectory))
            {
                throw new InvalidOperationException($"Directory '{_platform.UnitFileDirectory}' does not exist.");
            }

            string testFilePath = Path.Combine(_platform.UnitFileDirectory, $".updaemon-init-check-{Guid.NewGuid()}");
            try
            {
                File.WriteAllText(testFilePath, string.Empty);
            }
            finally
            {
                try { File.Delete(testFilePath); } catch { /* best-effort cleanup of probe file */ }
            }
            return Task.CompletedTask;
        }

        private async Task EnsureTemplateExistsAsync(CancellationToken cancellationToken = default)
        {
            if (File.Exists(_templateFilePath))
            {
                return;
            }

            Directory.CreateDirectory(_configDirectory);

            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream? resourceStream = assembly.GetManifestResourceStream(_platform.EmbeddedResourceName))
            {
                if (resourceStream == null)
                {
                    throw new InvalidOperationException($"Embedded resource '{_platform.EmbeddedResourceName}' not found.");
                }

                using (FileStream fileStream = new FileStream(_templateFilePath, FileMode.Create, FileAccess.Write))
                {
                    await resourceStream.CopyToAsync(fileStream, cancellationToken);
                }
            }
        }

    }
}
