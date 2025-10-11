using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    public class MockUnitFileManager : IUnitFileManager
    {
        public string? TemplateContent { get; set; }
        public string? TemplateWithSubstitutions { get; set; }

        public Task<string> ReadTemplateAsync()
        {
            return Task.FromResult(TemplateContent ?? string.Empty);
        }

        public Task<string> ReadTemplateWithSubstitutionsAsync(string serviceName, string executablePath)
        {
            return Task.FromResult(TemplateWithSubstitutions ?? string.Empty);
        }
    }
}

