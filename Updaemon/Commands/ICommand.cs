namespace Updaemon.Commands
{
    /// <summary>
    /// Interface for all command implementations.
    /// </summary>
    public interface ICommand
    {
        /// <summary>
        /// Gets the command name (e.g., "update", "new").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets a short description of the command for the main help menu.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Gets the usage string for the command (e.g., "updaemon update [app-name]").
        /// </summary>
        string Usage { get; }

        /// <summary>
        /// Executes the command with the provided arguments.
        /// </summary>
        /// <param name="args">Command arguments (excluding the command name itself).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Exit code (0 for success, non-zero for error).</returns>
        Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken);

        /// <summary>
        /// Gets detailed help text for this command.
        /// </summary>
        /// <returns>Detailed help text.</returns>
        string GetDetailedHelp();
    }
}

