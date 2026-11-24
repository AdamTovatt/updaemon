using System.ComponentModel;
using Updaemon.Configuration;
using Updaemon.IO;
using Updaemon.Models;

namespace Updaemon.Services
{
    /// <summary>
    /// Asks a version of a host whether it would load a version of an extension: runs its executable with
    /// its <c>check</c> arguments and the extension's folder. That may load the extension's code, so it runs
    /// as the user the host runs as, with none of the host's settings and none of updaemon's environment.
    /// </summary>
    public class ExtensionCheck
    {
        /// <summary>No answer in this time is a refusal.</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        private const string StandardPath = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";

        /// <summary>As much of the check's output as a message and the last run's record hold.</summary>
        private const int MaxReasonLength = 1000;

        private readonly UpdaemonScope _scope;

        public ExtensionCheck(UpdaemonScope scope)
        {
            _scope = scope;
        }

        internal Func<ProcessInvocation, CancellationToken, Task<ProcessRunner.Result>> Run { get; init; } = ProcessRunner.RunAsync;

        /// <summary>Why <paramref name="hostVersion"/> would not load the extension in <paramref name="extensionDirectory"/>; null when it would.</summary>
        public async Task<string?> FindRefusalAsync(InstalledApp host, DeployableVersion hostVersion, string extensionDirectory, CancellationToken cancellationToken = default)
        {
            ExtensionsSpec? extensions = hostVersion.Manifest.Extensions;
            if (extensions == null)
            {
                return $"{host.Name} {hostVersion.Version} takes no extensions";
            }

            if (extensions.Check == null)
            {
                return null;
            }

            string user = host.RunAs ?? (_scope.IsSystem ? "root" : _scope.UserName!);
            string? home = LibC.FindUser(user)?.Home;
            if (home == null && host.RunAs != null)
            {
                return $"its check cannot run: there is no user named '{host.RunAs}'";
            }

            ProcessInvocation invocation = CreateInvocation(host, hostVersion, extensions.Check, extensionDirectory, home);
            try
            {
                ProcessRunner.Result result = await Run(invocation, cancellationToken);
                return result.Success ? null : Reason(result);
            }
            catch (TimeoutException)
            {
                return $"its check gave no answer in {Timeout.TotalSeconds} seconds";
            }
            catch (Win32Exception ex)
            {
                return $"its check cannot run: {ex.Message.TrimEnd('.')}";
            }
        }

        private static ProcessInvocation CreateInvocation(InstalledApp host, DeployableVersion hostVersion, IReadOnlyList<string> check, string extensionDirectory, string? home)
        {
            Dictionary<string, string> environment = new Dictionary<string, string> { ["PATH"] = StandardPath };
            if (home != null)
            {
                environment["HOME"] = home;
            }

            return new ProcessInvocation(hostVersion.ExecutablePath, [.. check, extensionDirectory])
            {
                WorkingDirectory = hostVersion.Directory,
                Environment = environment,
                User = host.RunAs,
                Timeout = Timeout,
                WithoutInput = true,
                MaxOutput = 4 * MaxReasonLength,
            };
        }

        /// <summary>The check's output, on one line: it goes to the terminal and the run's record.</summary>
        private static string Reason(ProcessRunner.Result result)
        {
            string text = SafeText.ToLine($"{result.StdErr}\n{result.StdOut}", MaxReasonLength);
            return text.Length == 0 ? $"its check exited {result.ExitCode} without saying why" : text;
        }
    }
}
