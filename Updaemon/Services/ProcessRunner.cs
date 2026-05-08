using System.Diagnostics;

namespace Updaemon.Services
{
    /// <summary>
    /// Shared process-execution helpers used by the systemd / launchd wrappers.
    /// Both stdout and stderr are read concurrently with WaitForExitAsync so the helpers
    /// don't deadlock on processes that produce more output than the OS pipe buffer can hold.
    /// </summary>
    internal static class ProcessRunner
    {
        public readonly record struct Result(int ExitCode, string StdOut, string StdErr)
        {
            public bool Success => ExitCode == 0;
        }

        public static async Task<Result> RunAsync(string command, string[] arguments, CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using (Process process = Process.Start(startInfo)!)
            {
                Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                Task<string> stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

                await process.WaitForExitAsync(cancellationToken);

                string stdOut = await stdOutTask;
                string stdErr = await stdErrTask;

                return new Result(process.ExitCode, stdOut, stdErr);
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
    }
}
