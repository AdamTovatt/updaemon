using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IOutputWriter for testing.
    /// </summary>
    public class MockOutputWriter : IOutputWriter
    {
        private readonly List<string> _messages = new List<string>();
        private readonly List<string> _errors = new List<string>();

        public IReadOnlyList<string> Messages => _messages.AsReadOnly();
        public IReadOnlyList<string> Errors => _errors.AsReadOnly();

        public void WriteLine(string message)
        {
            _messages.Add(message);
        }

        public void WriteError(string message)
        {
            _errors.Add(message);
        }

        public void Clear()
        {
            _messages.Clear();
            _errors.Clear();
        }
    }
}

