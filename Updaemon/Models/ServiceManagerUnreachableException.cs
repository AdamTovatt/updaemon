namespace Updaemon.Models
{
    /// <summary>
    /// This user scope's service manager (systemd's user manager, or the user's launchd domain) cannot
    /// be reached, so nothing that needs it is written. The message is one line: why, and the fix.
    /// </summary>
    public class ServiceManagerUnreachableException : Exception
    {
        /// <summary>The exit code of a command this stops; install.sh tells it apart from other failures.</summary>
        public const int ExitCode = 3;

        public ServiceManagerUnreachableException(string message)
            : base(message)
        {
        }
    }
}
