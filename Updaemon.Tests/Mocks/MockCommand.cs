using Updaemon.Commands;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of ICommand for testing.
    /// </summary>
    public class MockCommand : ICommand
    {
        public string Name { get; set; } = "mock";
        public string Description { get; set; } = "Mock command";
        public string Usage { get; set; } = "updaemon mock";
        public string DetailedHelp { get; set; } = "Mock command help";
        public string[]? ReceivedArgs { get; private set; }
        public int ReturnCode { get; set; } = 0;
        public Exception? ExceptionToThrow { get; set; }

        public Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ReceivedArgs = args;
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            return Task.FromResult(ReturnCode);
        }

        public string GetDetailedHelp()
        {
            return DetailedHelp;
        }
    }
}

