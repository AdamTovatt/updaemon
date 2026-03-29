using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    public class MockUnitFileManager : IUnitFileManager
    {
        public string? TemplateContent { get; set; }
        public string? TemplateWithSubstitutions { get; set; }

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
    }
}

