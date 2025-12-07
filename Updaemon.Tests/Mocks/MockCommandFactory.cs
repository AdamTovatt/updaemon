using Updaemon.Commands;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of ICommandFactory for testing.
    /// </summary>
    public class MockCommandFactory : ICommandFactory
    {
        private readonly Dictionary<string, ICommand> _commands = new Dictionary<string, ICommand>();

        public void RegisterCommand(string name, ICommand command)
        {
            _commands[name] = command;
        }

        public ICommand GetCommand(string name)
        {
            if (!_commands.TryGetValue(name, out ICommand? command))
            {
                throw new UnknownCommandException(name);
            }
            return command;
        }

        public IEnumerable<ICommand> GetAllCommands()
        {
            return _commands.Values;
        }
    }
}

