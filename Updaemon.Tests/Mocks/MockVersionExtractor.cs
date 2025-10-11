using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    public class MockVersionExtractor : IVersionExtractor
    {
        public Version? ExtractVersionFromPathResult { get; set; }
        public string? LastExtractedPath { get; private set; }
        public int ExtractVersionFromPathCallCount { get; private set; }

        public Version? ExtractVersionFromPath(string? path)
        {
            LastExtractedPath = path;
            ExtractVersionFromPathCallCount++;
            return ExtractVersionFromPathResult;
        }
    }
}

