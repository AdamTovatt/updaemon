using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// How commands report a failure: one line on stderr, and exit code 1.
    /// </summary>
    public static class CommandErrors
    {
        /// <summary>Writes <c>Error: {message}</c>; returns the exit code.</summary>
        public static int Fail(this IOutputWriter outputWriter, string message)
        {
            outputWriter.WriteError($"Error: {message}");
            return 1;
        }

        /// <summary>Writes the command's usage, for arguments that do not fit it; returns the exit code.</summary>
        public static int FailUsage(this IOutputWriter outputWriter, ICommand command)
        {
            outputWriter.WriteError($"Usage: {command.Usage}");
            return 1;
        }

        public static int FailUnknownOption(this IOutputWriter outputWriter, string option) =>
            outputWriter.Fail($"Unknown option '{option}'.");

        /// <summary>
        /// How set, unset and set-args end: 0, or when the service they restarted did not stay running,
        /// a failure naming the logs. The change itself is kept either way.
        /// </summary>
        public static int FinishSettingsChange(this IOutputWriter outputWriter, string appName, bool staysRunning) =>
            staysRunning ? 0 : outputWriter.Fail($"{appName} did not stay running after the restart. The change is kept; see 'updaemon logs {appName}'.");
    }
}
