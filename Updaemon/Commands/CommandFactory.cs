using Microsoft.Extensions.DependencyInjection;

namespace Updaemon.Commands
{
    /// <summary>
    /// Factory for creating command instances. AOT-friendly explicit registration.
    /// </summary>
    public class CommandFactory : ICommandFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<string, Type> _commandTypes;

        public CommandFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

            // Explicit, AOT-friendly registration
            _commandTypes = new Dictionary<string, Type>
            {
                ["install"] = typeof(InstallCommand),
                ["update"] = typeof(UpdateCommand),
                ["set"] = typeof(SetCommand),
                ["unset"] = typeof(UnsetCommand),
                ["set-args"] = typeof(SetArgsCommand),
                ["set-restart"] = typeof(SetRestartCommand),
                ["remove"] = typeof(RemoveCommand),
                ["rollback"] = typeof(RollbackCommand),
                ["status"] = typeof(StatusCommand),
                ["logs"] = typeof(LogsCommand),
                ["dist-install"] = typeof(DistInstallCommand),
                ["self-update"] = typeof(SelfUpdateCommand),
                ["dist-list"] = typeof(DistListCommand),
                ["secret-set"] = typeof(SecretSetCommand),
                ["timer"] = typeof(TimerCommand),
            };
        }

        public ICommand GetCommand(string name)
        {
            if (!_commandTypes.TryGetValue(name, out Type? commandType))
            {
                throw new UnknownCommandException(name);
            }

            return (ICommand)_serviceProvider.GetRequiredService(commandType);
        }

        public IEnumerable<ICommand> GetAllCommands()
        {
            // Create instances to get metadata (for help display)
            foreach (Type commandType in _commandTypes.Values)
            {
                yield return (ICommand)_serviceProvider.GetRequiredService(commandType);
            }
        }
    }
}

