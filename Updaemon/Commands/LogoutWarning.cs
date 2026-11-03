using Updaemon.Interfaces;

namespace Updaemon.Commands
{
    /// <summary>
    /// The warning <c>install</c> of a service and <c>status</c> give when this scope's services stop at
    /// logout. updaemon never turns lingering on itself, since that takes root.
    /// </summary>
    internal static class LogoutWarning
    {
        public static string Text(string user) =>
            $"Warning: lingering is off for {user}, so your services stop when you log out. " +
            $"To keep them running, run: sudo loginctl enable-linger {user}";

        public static async Task WriteIfNeededAsync(IServiceManager serviceManager, IOutputWriter outputWriter, CancellationToken cancellationToken)
        {
            if (await serviceManager.ServicesStopAtLogoutAsync(cancellationToken))
            {
                outputWriter.WriteError(Text(Environment.UserName));
            }
        }
    }
}
