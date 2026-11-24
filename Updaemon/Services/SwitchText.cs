using Updaemon.Interfaces;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>How commands word a <see cref="SwitchOutcome"/>: one version by itself, several with their names.</summary>
    public static class SwitchText
    {
        /// <summary>Says why the service was not restarted, if it was not and there is one.</summary>
        public static void WriteRestartSkipped(this IOutputWriter outputWriter, string appName, SwitchOutcome outcome)
        {
            switch (outcome.Restart)
            {
                case RestartDecision.Manual:
                    string running = outcome.Switched.OfType<PackageSwitch.Moved>().FirstOrDefault(package => package.Name is AppName)?.From.ToString() ?? "the extensions it started with";
                    outputWriter.WriteLine($"{appName}: restart skipped (restart=manual); the service runs {running} until it restarts.");
                    break;
                case RestartDecision.Disabled:
                    outputWriter.WriteLine($"{appName}: restart skipped, as its service is disabled.");
                    break;
            }
        }

        /// <summary>
        /// What failed of the switch, what runs and where to look, whatever its <see cref="AppSwitch.IfDown"/>;
        /// null when nothing did.
        /// </summary>
        /// <param name="ifStands">A sentence for one that stands though the service is down; none when empty.</param>
        public static string? Problem(string appName, SwitchOutcome outcome, string ifStands = "")
        {
            string logs = HealthText.SeeLogs(appName);
            string stands = ifStands.Length == 0 ? logs : $"{ifStands} {logs}";
            return outcome switch
            {
                SwitchOutcome.Stands => null,
                SwitchOutcome.StandsDown { RestartFailure: string failure } => $"{appName} could not be restarted: {failure.TrimEnd('.')}. {stands}",
                SwitchOutcome.StandsDown => $"{HealthText.AfterRestart(appName)}. {stands}",
                SwitchOutcome.WentBack wentBack => $"{WentBack(appName, wentBack)}. {logs}",
                SwitchOutcome.NotBack notBack => $"{Failed(appName, notBack)}, and going back{(Before(notBack) is string before ? $" to {before}" : string.Empty)} failed: {notBack.Problem.TrimEnd('.')}. {logs}",
                _ => throw new InvalidOperationException($"There is no wording for {outcome.GetType().Name}."),
            };
        }

        private static string WentBack(string appName, SwitchOutcome.WentBack outcome)
        {
            string failed = Failed(appName, outcome);
            if (Before(outcome) is not string before)
            {
                return outcome.StaysUp == true
                    ? $"{failed}, so it is removed again"
                    : $"{failed}; it is removed again, but {appName} did not stay running without it either";
            }

            bool one = IsOne(outcome);
            string skipped = one ? To(outcome) : "each of the new versions";
            string skip = outcome.SkipNotRecorded == null
                ? $"{skipped} is skipped"
                : $"{skipped} could not be recorded as skipped ({outcome.SkipNotRecorded.TrimEnd('.')}), so the next update tries it again";
            string isCurrent = $"{before} {(one ? "is" : "are")} current again";
            return outcome.StaysUp switch
            {
                true => $"{failed}, so {before} {(one ? "runs" : "run")} again and {skip}",
                false => $"{failed}; {isCurrent} but did not stay running either, and {skip}",
                null => $"{failed}, so {isCurrent} and {skip}",
            };
        }

        private static string Failed(string appName, SwitchOutcome outcome) => outcome.Switched is [{ Package: AppName }]
            ? HealthText.DidNotStayRunning(To(outcome))
            : $"{HealthText.DidNotStayRunning(appName)} with {To(outcome)}";

        private static string To(SwitchOutcome outcome) => string.Join(", ", outcome.Switched.SelectMany(package => package switch
        {
            PackageSwitch.Moved moved => new[] { Name(outcome, package, moved.To) },
            PackageSwitch.Installed installed => new[] { Name(outcome, package, installed.To) },
            _ => Array.Empty<string>(),
        }));

        /// <summary>Null when all the switch did was install an extension.</summary>
        private static string? Before(SwitchOutcome outcome)
        {
            string[] versions = outcome.Switched.OfType<PackageSwitch.Moved>().Select(moved => Name(outcome, moved, moved.From)).ToArray();
            return versions.Length == 0 ? null : string.Join(", ", versions);
        }

        private static string Name(SwitchOutcome outcome, PackageSwitch package, Version version) =>
            IsOne(outcome) ? version.ToString() : $"{package.Package} {version}";

        private static bool IsOne(SwitchOutcome outcome) => outcome.Switched.Count == 1;
    }
}
