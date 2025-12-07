namespace Updaemon.Commands
{
    /// <summary>
    /// Factory for creating command instances.
    /// </summary>
    public interface ICommandFactory
    {
        /// <summary>
        /// Gets a command instance by name.
        /// </summary>
        /// <param name="name">The command name.</param>
        /// <returns>The command instance.</returns>
        /// <exception cref="UnknownCommandException">Thrown when the command is not found.</exception>
        ICommand GetCommand(string name);

        /// <summary>
        /// Gets all registered commands.
        /// </summary>
        /// <returns>All registered command instances.</returns>
        IEnumerable<ICommand> GetAllCommands();
    }
}

