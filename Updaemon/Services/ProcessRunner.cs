using System.Diagnostics;
using System.Text;

namespace Updaemon.Services
{
    /// <summary>A command to run directly, each argument passed as is (no shell).</summary>
    internal sealed record ProcessInvocation(string Command, IReadOnlyList<string> Arguments)
    {
        /// <summary>Null for updaemon's own.</summary>
        public string? WorkingDirectory { get; init; }

        /// <summary>All the variables it gets; null for updaemon's own environment.</summary>
        public IReadOnlyDictionary<string, string>? Environment { get; init; }

        /// <summary>The user it runs as, which takes root; null for the one updaemon runs as.</summary>
        public string? User { get; init; }

        /// <summary>How long it may take before it is killed; null for as long as it wants.</summary>
        public TimeSpan? Timeout { get; init; }

        /// <summary>Whether it gets no input, rather than updaemon's.</summary>
        public bool WithoutInput { get; init; }

        /// <summary>How many characters of its stdout, and of its stderr, are kept; null for all of them. The rest is read and dropped.</summary>
        public int? MaxOutput { get; init; }
    }

    /// <summary>
    /// Runs the external commands updaemon uses (systemctl, launchctl and the like), a newly downloaded
    /// updaemon's smoke run and a host's check of its extensions. Both stdout and stderr are read concurrently with
    /// WaitForExitAsync so the helpers don't deadlock on processes that produce more output than the
    /// OS pipe buffer can hold.
    /// </summary>
    internal static class ProcessRunner
    {
        public readonly record struct Result(int ExitCode, string StdOut, string StdErr)
        {
            public bool Success => ExitCode == 0;
        }

        /// <summary>Runs the command to its end and returns what it printed. Cancelling kills it.</summary>
        public static Task<Result> RunAsync(string command, string[] arguments, CancellationToken cancellationToken) =>
            RunAsync(new ProcessInvocation(command, arguments), cancellationToken);

        /// <summary>Runs the command to its end and returns what it printed. Cancelling kills it, and so does its timeout.</summary>
        /// <exception cref="TimeoutException">It did not end within its timeout.</exception>
        /// <exception cref="System.ComponentModel.Win32Exception">It could not be started.</exception>
        public static async Task<Result> RunAsync(ProcessInvocation invocation, CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = StartInfo(invocation);
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;

            using (CancellationTokenSource end = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (Process process = Process.Start(startInfo)!)
            {
                if (invocation.Timeout is TimeSpan timeout)
                {
                    end.CancelAfter(timeout);
                }

                if (invocation.WithoutInput)
                {
                    process.StandardInput.Close();
                }

                Task<string> stdOutTask = ReadAsync(process.StandardOutput, invocation.MaxOutput, end.Token);
                Task<string> stdErrTask = ReadAsync(process.StandardError, invocation.MaxOutput, end.Token);

                try
                {
                    await process.WaitForExitAsync(end.Token);
                    return new Result(process.ExitCode, await stdOutTask, await stdErrTask);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException($"{invocation.Command} did not end within {invocation.Timeout!.Value.TotalSeconds} seconds.");
                }
            }
        }

        private static async Task<string> ReadAsync(StreamReader reader, int? max, CancellationToken cancellationToken)
        {
            if (max == null)
            {
                return await reader.ReadToEndAsync(cancellationToken);
            }

            StringBuilder kept = new StringBuilder();
            char[] buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer, cancellationToken)) > 0)
            {
                kept.Append(buffer, 0, Math.Min(read, max.Value - kept.Length));
            }

            return kept.ToString();
        }

        /// <summary>
        /// Runs a command on updaemon's own terminal (stdin, stdout and stderr inherited) and returns
        /// its exit code. Cancellation (Ctrl+C, SIGTERM or SIGHUP to updaemon) kills the command and
        /// ends normally: that is how a followed log is stopped.
        /// </summary>
        public static async Task<int> RunAttachedAsync(string command, string[] arguments, CancellationToken cancellationToken)
        {
            using (Process process = Process.Start(StartInfo(new ProcessInvocation(command, arguments)))!)
            {
                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                    return process.ExitCode;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }

                    return 0;
                }
            }
        }

        /// <summary>
        /// Convenience wrapper that throws if the process exits non-zero, including the
        /// command line and stderr text in the exception message for diagnostics.
        /// </summary>
        public static async Task<Result> RunAndCheckAsync(string command, string[] arguments, CancellationToken cancellationToken)
        {
            Result result = await RunAsync(command, arguments, cancellationToken);
            if (!result.Success)
            {
                string args = string.Join(' ', arguments);
                string detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
                throw new InvalidOperationException($"{command} {args} failed (exit {result.ExitCode}): {detail.Trim()}");
            }
            return result;
        }

        internal static ProcessStartInfo StartInfo(ProcessInvocation invocation)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = invocation.Command,
                UseShellExecute = false,
                WorkingDirectory = invocation.WorkingDirectory ?? string.Empty,
                RedirectStandardInput = invocation.WithoutInput,
            };
            foreach (string arg in invocation.Arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }

            if (invocation.User != null)
            {
                // .NET switches to the user's id and groups itself between fork and exec, on Linux and macOS alike.
                startInfo.UserName = invocation.User;
            }

            if (invocation.Environment != null)
            {
                startInfo.Environment.Clear();
                foreach (KeyValuePair<string, string> variable in invocation.Environment)
                {
                    startInfo.Environment[variable.Key] = variable.Value;
                }
            }

            return startInfo;
        }
    }
}
