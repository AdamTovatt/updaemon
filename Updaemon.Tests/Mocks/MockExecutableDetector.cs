using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IExecutableDetector with configurable results.
    /// </summary>
    public class MockExecutableDetector : IExecutableDetector
    {
        public List<string> MethodCalls { get; } = new List<string>();
        public Dictionary<string, string?> ConfiguredResults { get; } = new Dictionary<string, string?>();

        public Task<string?> FindExecutableAsync(string directoryPath, string serviceName)
        {
            string key = $"{directoryPath}:{serviceName}";
            MethodCalls.Add($"{nameof(FindExecutableAsync)}:{key}");

            if (ConfiguredResults.TryGetValue(key, out string? result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult<string?>(null);
        }

        public void SetExecutableResult(string directoryPath, string serviceName, string? executablePath)
        {
            string key = $"{directoryPath}:{serviceName}";
            ConfiguredResults[key] = executablePath;
        }
    }
}

