using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    public class MockDownloadPostProcessor : IDownloadPostProcessor
    {
        public List<string> ProcessedDirectories { get; } = new List<string>();
        public bool ShouldThrow { get; set; }

        public Task ProcessAsync(string targetDirectory)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Mock post-processor error");
            }

            ProcessedDirectories.Add(targetDirectory);
            return Task.CompletedTask;
        }
    }
}

