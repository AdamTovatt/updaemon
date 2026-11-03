using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Finds an executable by name in a release that ships no manifest.
    /// </summary>
    public class ExecutableDetector : IExecutableDetector
    {
        public Task<string?> FindExecutableAsync(string directoryPath, string executableName, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                return Task.FromResult<string?>(null);
            }

            string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

            // First, try exact name match
            string? exactMatch = files.FirstOrDefault(f =>
                Path.GetFileName(f).Equals(executableName, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
            {
                return Task.FromResult<string?>(exactMatch);
            }

            // Then try partial name match
            string? partialMatch = files.FirstOrDefault(f =>
                Path.GetFileName(f).Contains(executableName, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(partialMatch);
        }
    }
}
