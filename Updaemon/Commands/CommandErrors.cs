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
        /// A service failed its health check: <c>{version} did not stay running{consequence}.</c>, then
        /// where to look. The caller names the app in front.
        /// </summary>
        public static string NotRunning(string appName, Version version, string consequence = "") =>
            $"{version} did not stay running{consequence}. See 'updaemon logs {appName}'.";
    }
}
