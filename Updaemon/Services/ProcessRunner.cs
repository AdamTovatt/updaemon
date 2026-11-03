using System.Diagnostics;

namespace Updaemon.Services
{
    /// <summary>
    /// Runs the external commands updaemon uses: systemctl, launchctl and the like, and a newly
    /// downloaded updaemon's smoke run. Both stdout and stderr are read concurrently with
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
        public static async Task<Result> RunAsync(string command, string[] arguments, CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = StartInfo(command, arguments);
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;

            using (Process process = Process.Start(startInfo)!)
            {
                Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                Task<string> stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    throw;
                }

                string stdOut = await stdOutTask;
                string stdErr = await stdErrTask;

                return new Result(process.ExitCode, stdOut, stdErr);
            }
        }

        /// <summary>
        /// Runs a command on updaemon's own terminal (stdin, stdout and stderr inherited) and returns
        /// its exit code. Cancellation (Ctrl+C, which the terminal also sends the command) ends it
        /// normally: that is how a followed log is stopped.
        /// </summary>
        public static async Task<int> RunAttachedAsync(string command, string[] arguments, CancellationToken cancellationToken)
        {
            using (Process process = Process.Start(StartInfo(command, arguments))!)
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

        /// <summary>Runs <paramref name="command"/> directly, each argument passed as is (no shell).</summary>
        private static ProcessStartInfo StartInfo(string command, string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
            };
            foreach (string arg in arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }

            return startInfo;
        }
    }
}
