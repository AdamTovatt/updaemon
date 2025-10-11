using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Console-based implementation of IOutputWriter.
    /// </summary>
    public class ConsoleOutputWriter : IOutputWriter
    {
        public void WriteLine(string message)
        {
            Console.Out.WriteLine(message);
        }

        public void WriteError(string message)
        {
            Console.Error.WriteLine(message);
        }
    }
}

