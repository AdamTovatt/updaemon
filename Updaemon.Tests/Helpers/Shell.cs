using System.Diagnostics;

namespace Updaemon.Tests.Helpers
{
    /// <summary>
    /// Runs a script with /bin/sh, the shell generated wrappers are written for.
    /// </summary>
    public static class Shell
    {
        public static async Task<string> RunAsync(string scriptPath, params string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo("/bin/sh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(scriptPath);
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using (Process process = Process.Start(startInfo)!)
            {
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                Assert.True(process.ExitCode == 0, $"sh exited {process.ExitCode}: {error}");
                return output;
            }
        }

        /// <summary>A script that prints each named variable as NAME=value, then its arguments, one per line.</summary>
        public static string PrintEnvironmentScript(params string[] variableNames)
        {
            IEnumerable<string> lines = variableNames.Select(name => $"printf '%s\\n' \"{name}=${name}\"");
            return "#!/bin/sh\n" + string.Join("\n", lines) + "\nfor a in \"$@\"; do printf 'arg=%s\\n' \"$a\"; done\n";
        }
    }
}
