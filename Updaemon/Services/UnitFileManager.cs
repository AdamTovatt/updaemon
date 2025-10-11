using System.Reflection;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Manages systemd unit file templates and generation.
    /// </summary>
    public class UnitFileManager : IUnitFileManager
    {
        private const string ConfigDirectory = "/var/lib/updaemon";
        private const string TemplateFileName = "default-unit.template";
        private const string EmbeddedResourceName = "Updaemon.Templates.service.template";

        private readonly string _configDirectory;
        private readonly string _templateFilePath;

        public UnitFileManager()
        {
            _configDirectory = ConfigDirectory;
            _templateFilePath = Path.Combine(_configDirectory, TemplateFileName);
        }

        public UnitFileManager(string configDirectory)
        {
            _configDirectory = configDirectory;
            _templateFilePath = Path.Combine(_configDirectory, TemplateFileName);
        }

        public async Task<string> ReadTemplateAsync(CancellationToken cancellationToken = default)
        {
            await EnsureTemplateExistsAsync(cancellationToken);
            return await File.ReadAllTextAsync(_templateFilePath, cancellationToken);
        }

        public async Task<string> ReadTemplateWithSubstitutionsAsync(string serviceName, string executablePath, CancellationToken cancellationToken = default)
        {
            string template = await ReadTemplateAsync(cancellationToken);

            string result = template
                .Replace("{SERVICE_NAME}", serviceName)
                .Replace("{DESCRIPTION}", $"{serviceName} service managed by updaemon")
                .Replace("{EXECUTABLE_PATH}", executablePath);

            return result;
        }

        private async Task EnsureTemplateExistsAsync(CancellationToken cancellationToken = default)
        {
            if (File.Exists(_templateFilePath))
                return;

            Directory.CreateDirectory(_configDirectory);

            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream? resourceStream = assembly.GetManifestResourceStream(EmbeddedResourceName))
            {
                if (resourceStream == null)
                {
                    throw new InvalidOperationException($"Embedded resource '{EmbeddedResourceName}' not found.");
                }

                using (FileStream fileStream = new FileStream(_templateFilePath, FileMode.Create, FileAccess.Write))
                {
                    await resourceStream.CopyToAsync(fileStream, cancellationToken);
                }
            }
        }
    }
}

