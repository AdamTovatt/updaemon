using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>How messages name a switch that did not stay running: one version by itself, several with their names.</summary>
    internal sealed class SwitchText
    {
        private readonly string _appName;
        private readonly string _failed;

        /// <summary>Empty when all the switch did was install an extension.</summary>
        private readonly string _before;
        private readonly bool _one;
        private readonly string _skipped;

        public SwitchText(string appName, IReadOnlyList<PackageSwitch> switched)
        {
            _appName = appName;
            _one = switched.Count == 1;
            string to = Join(switched, package => package.To);
            _failed = _one && switched[0].Name is AppName ? $"{to} did not stay running" : $"{appName} did not stay running with {to}";
            _before = Join(switched.Where(package => package.From != null).ToList(), package => package.From!);
            _skipped = _one ? to : "each of the new versions";
        }

        private string Join(IReadOnlyList<PackageSwitch> packages, Func<PackageSwitch, Version> version) =>
            string.Join(", ", packages.Select(package => _one ? version(package).ToString() : $"{package.Name} {version(package)}"));

        public string PuttingBackFailed(string problem) =>
            $"{_failed}, and going back{(_before.Length == 0 ? string.Empty : $" to {_before}")} failed: {problem.TrimEnd('.')}. {ServiceDeployer.SeeLogs(_appName)}";

        /// <param name="staysRunning">Whether the service stays up on what is current again; null when that is none.</param>
        /// <param name="skipNotRecorded">Why the new versions could not be recorded as skipped; null when they were.</param>
        public string PutBack(bool? staysRunning, string? skipNotRecorded)
        {
            string logs = ServiceDeployer.SeeLogs(_appName);
            if (_before.Length == 0)
            {
                return staysRunning == true
                    ? $"{_failed}, so it is removed again. {logs}"
                    : $"{_failed}; it is removed again, but {_appName} did not stay running without it either. {logs}";
            }

            string skip = skipNotRecorded == null
                ? $"{_skipped} is skipped"
                : $"{_skipped} could not be recorded as skipped ({skipNotRecorded.TrimEnd('.')}), so the next update tries it again";
            string isCurrent = $"{_before} {(_one ? "is" : "are")} current again";
            return staysRunning switch
            {
                true => $"{_failed}, so {_before} {(_one ? "runs" : "run")} again and {skip}. {logs}",
                false => $"{_failed}; {isCurrent} but did not stay running either, and {skip}. {logs}",
                null => $"{_failed}, so {isCurrent} and {skip}. {logs}",
            };
        }
    }
}
