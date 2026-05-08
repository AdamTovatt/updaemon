using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    public class MockUnitFileManager : IUnitFileManager
    {
        public string? TemplateContent { get; set; }
        public string? TemplateWithSubstitutions { get; set; }

        /// <summary>Override in tests when the path matters.</summary>
        public string UnitFileDirectory { get; set; } = "/etc/systemd/system";
        public string UnitFileExtension { get; set; } = ".service";

        /// <summary>If set to false, EnsureWritableAsync throws UnauthorizedAccessException.</summary>
        public bool IsWritable { get; set; } = true;

        public Task<string> ReadTemplateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TemplateContent ?? string.Empty);
        }

        public Task<string> ReadTemplateWithSubstitutionsAsync(string serviceName, string symlinkPath, string executableName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TemplateWithSubstitutions ?? string.Empty);
        }

        public async Task WriteUnitFileAsync(string unitFilePath, string serviceName, string symlinkPath, string executableName, CancellationToken cancellationToken = default)
        {
            string content = await ReadTemplateWithSubstitutionsAsync(serviceName, symlinkPath, executableName, cancellationToken);
            await File.WriteAllTextAsync(unitFilePath, content, cancellationToken);
        }

        public string GetUnitFilePath(string serviceName)
        {
            return Path.Combine(UnitFileDirectory, serviceName + UnitFileExtension);
        }

        public Task EnsureWritableAsync(CancellationToken cancellationToken = default)
        {
            if (!IsWritable)
            {
                throw new UnauthorizedAccessException($"Mock: directory '{UnitFileDirectory}' is not writable.");
            }
            return Task.CompletedTask;
        }
    }
}
