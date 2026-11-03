using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Answers prompts from a queue and records what was asked. With <see cref="CancelWhenAsked"/>,
    /// a prompt behaves like Ctrl+C at the terminal.
    /// </summary>
    public class MockPrompter : IPrompter
    {
        public bool IsInteractive { get; set; } = true;

        public bool CancelWhenAsked { get; set; }

        public Queue<string> Answers { get; } = new Queue<string>();

        public List<(string Prompt, bool Secret)> Asked { get; } = new List<(string, bool)>();

        public Task<string> ReadValueAsync(string prompt, bool secret, CancellationToken cancellationToken)
        {
            Asked.Add((prompt, secret));
            if (CancelWhenAsked)
            {
                throw new OperationCanceledException();
            }

            return Task.FromResult(Answers.Dequeue());
        }
    }
}
