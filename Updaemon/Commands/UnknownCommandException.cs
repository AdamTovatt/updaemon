namespace Updaemon.Commands
{
    /// <summary>
    /// Exception thrown when an unknown command is requested.
    /// </summary>
    public class UnknownCommandException : Exception
    {
        /// <summary>
        /// Gets the command name that was not found.
        /// </summary>
        public string CommandName { get; }

        public UnknownCommandException(string commandName)
            : base($"Unknown command: {commandName}")
        {
            CommandName = commandName;
        }
    }
}

