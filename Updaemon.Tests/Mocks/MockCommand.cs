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
        /// <summary>What <see cref="ChangesState"/> answers, whatever the arguments.</summary>
        public bool Changes { get; set; }

        /// <summary>The arguments <see cref="ChangesState"/> was last asked about.</summary>
        public string[]? ChangesStateArgs { get; private set; }

        public bool ChangesState(string[] args)
        {
            ChangesStateArgs = args;
            return Changes;
        }
        public string DetailedHelp { get; set; } = "Mock command help";
        public string[]? ReceivedArgs { get; private set; }
        public int ReturnCode { get; set; } = 0;
        public Exception? ExceptionToThrow { get; set; }
        public Action? OnExecute { get; set; }

        public Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ReceivedArgs = args;
            OnExecute?.Invoke();
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

