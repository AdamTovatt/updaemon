namespace Updaemon.Interfaces
{
    /// <summary>
    /// Asks the person at the terminal for a value.
    /// </summary>
    public interface IPrompter
    {
        /// <summary>Whether there is a terminal to ask; false when input is redirected.</summary>
        bool IsInteractive { get; }

        /// <summary>
        /// Shows <paramref name="prompt"/> and reads a line; with <paramref name="secret"/>, without
        /// echoing it. Stops waiting when <paramref name="cancellationToken"/> fires (Ctrl+C).
        /// </summary>
        Task<string> ReadValueAsync(string prompt, bool secret, CancellationToken cancellationToken);
    }
}
