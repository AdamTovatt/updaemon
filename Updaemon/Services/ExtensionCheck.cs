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
        private readonly VersionStore _versions;

        public ExtensionCheck(UpdaemonScope scope, VersionStore versions)
        {
            _scope = scope;
            _versions = versions;
        }

        internal Func<ProcessInvocation, CancellationToken, Task<ProcessRunner.Result>> Run { get; init; } = ProcessRunner.RunAsync;

        /// <summary>Whether <paramref name="hostVersion"/> would load that kept version of the extension.</summary>
        /// <exception cref="InvalidOperationException">The version is not kept; the message lists the kept ones.</exception>
        public async Task<ExtensionVerdict> AskAsync(
            InstalledApp host, DeployableVersion hostVersion, ExtensionName name, Version version, CancellationToken cancellationToken = default)
        {
            return await FindReasonAsync(host, hostVersion, _versions.OpenRetained(name, version), cancellationToken) is string reason
                ? new ExtensionRefusal(hostVersion.Version, name, version, reason)
                : new AcceptedExtension(hostVersion.Version, name, version);
        }

        private async Task<string?> FindReasonAsync(InstalledApp host, DeployableVersion hostVersion, string extensionDirectory, CancellationToken cancellationToken)
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
            LibC.User? found = LibC.FindUser(user);
            if (found == null && host.RunAs != null)
            {
                return $"its check cannot run: there is no user named '{host.RunAs}'";
            }

            ProcessInvocation invocation = CreateInvocation(host, hostVersion, extensions.Check, extensionDirectory, found);
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

        private static ProcessInvocation CreateInvocation(InstalledApp host, DeployableVersion hostVersion, IReadOnlyList<string> check, string extensionDirectory, LibC.User? user)
        {
            Dictionary<string, string> environment = new Dictionary<string, string> { ["PATH"] = StandardPath };
            if (user != null)
            {
                environment["HOME"] = user.Home;
            }

            return new ProcessInvocation(hostVersion.ExecutablePath, [.. check, extensionDirectory])
            {
                WorkingDirectory = hostVersion.Directory,
                Environment = environment,
                // Its name, which a process is started with, also where the host runs as a user id.
                User = host.RunAs == null ? null : user?.Name,
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
