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
                ["new"] = typeof(NewCommand),
                ["init"] = typeof(InitCommand),
                ["list"] = typeof(ListCommand),
                ["update"] = typeof(UpdateCommand),
                ["set-remote"] = typeof(SetRemoteCommand),
                ["set-exec-name"] = typeof(SetExecNameCommand),
                ["set-restart"] = typeof(SetRestartCommand),
                ["dist-install"] = typeof(DistInstallCommand),
                ["dist-update"] = typeof(DistUpdateCommand),
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

