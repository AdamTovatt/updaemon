namespace Updaemon.Tests.Helpers
{
    /// <summary>
    /// Helper for creating and cleaning up temporary directories and files for tests.
    /// </summary>
    public class TempFileHelper : IDisposable
    {
        private readonly string _tempDirectory;
        private bool _disposed;

        public TempFileHelper()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"updaemon_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectory);
        }

        public string TempDirectory => _tempDirectory;

        public string CreateTempDirectory(string relativePath)
        {
            string fullPath = Path.Combine(_tempDirectory, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateTempFile(string relativePath, string contents)
        {
            string fullPath = Path.Combine(_tempDirectory, relativePath);
            string? directoryPath = Path.GetDirectoryName(fullPath);
            if (directoryPath != null)
            {
                Directory.CreateDirectory(directoryPath);
            }
            File.WriteAllText(fullPath, contents);
            return fullPath;
        }

        public string GetFullPath(string relativePath)
        {
            return Path.Combine(_tempDirectory, relativePath);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}

