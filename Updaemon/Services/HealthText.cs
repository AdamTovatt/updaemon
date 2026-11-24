namespace Updaemon.Services
{
    /// <summary>How a service that did not pass its health check (<see cref="Interfaces.IServiceDeployer.StaysRunningAsync"/>) is worded.</summary>
    public static class HealthText
    {
        /// <summary>The start of a sentence, without its period.</summary>
        public static string DidNotStayRunning(string subject) => $"{subject} did not stay running";

        /// <summary><see cref="DidNotStayRunning"/> of an app whose versions need no naming.</summary>
        public static string AfterRestart(string appName) => $"{DidNotStayRunning(appName)} after the restart";

        /// <summary>One whole: what did not, <paramref name="consequence"/>, then where to look. The caller names the app in front.</summary>
        public static string Failed(string appName, Version version, string consequence = "") =>
            $"{DidNotStayRunning(version.ToString())}{consequence}. {SeeLogs(appName)}";

        /// <summary>Where the service says why.</summary>
        public static string SeeLogs(string appName) => $"See 'updaemon logs {appName}'.";
    }
}
