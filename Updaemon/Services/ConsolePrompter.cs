using System.Text;
using Updaemon.Interfaces;

namespace Updaemon.Services
{
    /// <summary>
    /// Reads values from the terminal; secrets are read key by key without echo.
    /// </summary>
    public class ConsolePrompter : IPrompter
    {
        public bool IsInteractive => !Console.IsInputRedirected;

        public Task<string> ReadValueAsync(string prompt, bool secret, CancellationToken cancellationToken)
        {
            Console.Out.Write(prompt);

            // A console read cannot be cancelled, and Ctrl+C only cancels the token (the process keeps
            // running so it can clean up). Read on a pool thread and stop waiting on cancellation; the
            // abandoned read ends with the process.
            return Task.Run(() => secret ? ReadHidden() : Console.ReadLine() ?? string.Empty, CancellationToken.None)
                .WaitAsync(cancellationToken);
        }

        private static string ReadHidden()
        {
            StringBuilder value = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.Out.WriteLine();
                    return value.ToString();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (value.Length > 0)
                    {
                        value.Length--;
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    value.Append(key.KeyChar);
                }
            }
        }
    }
}
