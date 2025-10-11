namespace Updaemon.Interfaces
{
    /// <summary>
    /// Provides an abstraction for writing output messages.
    /// </summary>
    public interface IOutputWriter
    {
        /// <summary>
        /// Writes a normal message.
        /// </summary>
        /// <param name="message">The message to write.</param>
        void WriteLine(string message);

        /// <summary>
        /// Writes an error message.
        /// </summary>
        /// <param name="message">The error message to write.</param>
        void WriteError(string message);
    }
}

