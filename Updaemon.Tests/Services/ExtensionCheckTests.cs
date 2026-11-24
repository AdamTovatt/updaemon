using System.Diagnostics;
using Updaemon.IO;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    /// <summary>The check a host runs on an extension: what is generated to run it, and real runs of it as this user.</summary>
    public class ExtensionCheckTests
    {
        private static readonly InstalledApp Host = new InstalledApp { Name = "shelf" };

        /// <summary>A host version whose check is <paramref name="script"/>.</summary>
        private static DeployableVersion HostVersion(TempFileHelper temp, string script = "exit 0", string manifest = AppHarness.HostManifest)
        {
            string executable = temp.CreateTempFile("1.0.0/bin/shelf", $"#!/bin/sh\n{script}\n");
            File.SetUnixFileMode(executable, AtomicFile.Executable);
            return new DeployableVersion
            {
                Version = new Version(1, 0, 0),
                Directory = temp.GetFullPath("1.0.0"),
                Manifest = AppManifest.Parse(manifest),
                ManifestInDirectory = true,
            };
        }

        private static Task<string?> RunAsync(TempFileHelper temp, string script) =>
            new ExtensionCheck(TestScope.Create(temp.TempDirectory)).FindRefusalAsync(Host, HostVersion(temp, script), temp.CreateTempDirectory("image plugin"));

        /// <summary>What would be run for a host in this scope, without running it.</summary>
        private static async Task<ProcessInvocation> InvocationAsync(TempFileHelper temp, bool isSystem, bool isMacOS, string? runAs)
        {
            ProcessInvocation? invocation = null;
            ExtensionCheck check = new ExtensionCheck(TestScope.Create(temp.TempDirectory, isSystem, isMacOS))
            {
                Run = (ran, _) =>
                {
                    invocation = ran;
                    return Task.FromResult(new ProcessRunner.Result(0, string.Empty, string.Empty));
                },
            };
            await check.FindRefusalAsync(new InstalledApp { Name = "shelf", RunAs = runAs }, HostVersion(temp), "/versions/images/2.0.0");
            return invocation!;
        }

        [Theory]
        [InlineData(true, false, "root", "root")]
        [InlineData(true, true, "root", "root")]
        [InlineData(true, false, null, null)]
        [InlineData(false, false, null, null)]
        [InlineData(false, true, null, null)]
        public async Task Check_RunsAsTheUserTheHostRunsAs_WithOnlyAPathAndItsHome(bool isSystem, bool isMacOS, string? runAs, string? expectedUser)
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                ProcessInvocation invocation = await InvocationAsync(temp, isSystem, isMacOS, runAs);
                ProcessStartInfo startInfo = ProcessRunner.StartInfo(invocation);

                Assert.Equal(temp.GetFullPath("1.0.0/bin/shelf"), startInfo.FileName);
                Assert.Equal(new[] { "--check-plugin", "/versions/images/2.0.0" }, startInfo.ArgumentList);
                Assert.Equal(temp.GetFullPath("1.0.0"), startInfo.WorkingDirectory);
                Assert.Equal(expectedUser ?? string.Empty, startInfo.UserName);
                Assert.False(startInfo.UseShellExecute);
                Assert.True(startInfo.RedirectStandardInput);
                Assert.Equal(TimeSpan.FromSeconds(60), invocation.Timeout);
                Assert.Equal(4000, invocation.MaxOutput);
                // The user scope's "ada" is in no user database, so only root has a home here.
                Dictionary<string, string?> expected = new Dictionary<string, string?> { ["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin" };
                if (isSystem)
                {
                    expected["HOME"] = LibC.FindUser("root")!.Home;
                }

                Assert.Equal(expected, startInfo.Environment);
            }
        }

        [Fact]
        public async Task Check_RunAsUserIsGone_RefusesWithoutRunning()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                ExtensionCheck check = new ExtensionCheck(TestScope.Create(temp.TempDirectory, isSystem: true))
                {
                    Run = (_, _) => throw new InvalidOperationException("It ran."),
                };

                string? refusal = await check.FindRefusalAsync(new InstalledApp { Name = "shelf", RunAs = "no such user" }, HostVersion(temp), "/x");

                Assert.Equal("its check cannot run: there is no user named 'no such user'", refusal);
            }
        }

        [Fact]
        public async Task Check_ExitsZero_Accepts()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                Assert.Null(await RunAsync(temp, $"[ \"$1\" = --check-plugin ] && [ \"$2\" = '{temp.GetFullPath("image plugin")}' ] && [ $# = 2 ]"));
            }
        }

        [Fact]
        public async Task Check_RealRun_HasNoEnvironmentOfUpdaemonsAndNoInput()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                const string variable = "UPDAEMON_TEST_AMBIENT";
                Environment.SetEnvironmentVariable(variable, "ambient");
                try
                {
                    // cat ends only because there is no input to wait for.
                    string? refusal = await RunAsync(temp, "cat; /usr/bin/env | /usr/bin/cut -d= -f1 | /usr/bin/sort | /usr/bin/tr '\\n' ' '; /bin/pwd -P; exit 3");

                    // PWD, SHLVL, OLDPWD and _ are the shell's own.
                    string[] variables = refusal!.Split(' ').Where(name => name is not ("PWD" or "SHLVL" or "OLDPWD" or "_" or "/")).ToArray();
                    Assert.Equal(new[] { "PATH", LibC.RealPath(temp.GetFullPath("1.0.0"))! }, variables);
                }
                finally
                {
                    Environment.SetEnvironmentVariable(variable, null);
                }
            }
        }

        [Theory]
        [InlineData("echo wrong ABI >&2; echo images 2.0.0; exit 1", "wrong ABI / images 2.0.0")]
        [InlineData("printf 'one\\r\\n\\033[31mtwo\\033[0m.\\n'; exit 1", "one / [31mtwo[0m")]
        [InlineData("exit 7", "its check exited 7 without saying why")]
        [InlineData("kill -9 $$", "its check exited 137 without saying why")]
        public async Task Check_AnythingButZero_RefusesWithItsOutputOnOneLine(string script, string expected)
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                Assert.Equal(expected, await RunAsync(temp, script));
            }
        }

        [Fact]
        public async Task Check_EndlessOutput_IsCut()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                string? refusal = await RunAsync(temp, "i=0; while [ $i -lt 500 ]; do echo 0123456789; i=$((i+1)); done; exit 1");

                Assert.Equal(1003, refusal!.Length);
                Assert.EndsWith("...", refusal);
            }
        }

        [Fact]
        public async Task Check_CannotBeStarted_Refuses()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                DeployableVersion version = HostVersion(temp);
                File.Delete(version.ExecutablePath);

                string? refusal = await new ExtensionCheck(TestScope.Create(temp.TempDirectory)).FindRefusalAsync(Host, version, "/x");

                Assert.StartsWith("its check cannot run: ", refusal);
            }
        }

        [Fact]
        public async Task Check_NoAnswerInTime_RefusesAndKillsIt()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                string pidFile = temp.GetFullPath("pid");
                ExtensionCheck check = new ExtensionCheck(TestScope.Create(temp.TempDirectory))
                {
                    Run = (invocation, token) => ProcessRunner.RunAsync(invocation with { Timeout = TimeSpan.FromSeconds(1) }, token),
                };

                string? refusal = await check.FindRefusalAsync(Host, HostVersion(temp, $"echo $$ > '{pidFile}'; exec sleep 30"), "/x");

                Assert.Equal("its check gave no answer in 60 seconds", refusal);
                Assert.Throws<ArgumentException>(() => Process.GetProcessById(int.Parse(File.ReadAllText(pidFile))));
            }
        }

        [Fact]
        public async Task Check_Cancelled_IsKilledAndNotARefusal()
        {
            using (TempFileHelper temp = new TempFileHelper())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                string pidFile = temp.GetFullPath("pid");
                Task<string?> check = new ExtensionCheck(TestScope.Create(temp.TempDirectory))
                    .FindRefusalAsync(Host, HostVersion(temp, $"echo $$ > '{pidFile}'; exec sleep 30"), "/x", cancellation.Token);
                for (int i = 0; i < 200 && !(File.Exists(pidFile) && File.ReadAllText(pidFile).EndsWith('\n')); i++)
                {
                    await Task.Delay(50);
                }

                cancellation.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
                Assert.Throws<ArgumentException>(() => Process.GetProcessById(int.Parse(File.ReadAllText(pidFile))));
            }
        }

        /// <summary>For real, from files only root could read before updaemon made them readable. Needs root, to become another user.</summary>
        [SkippableFact]
        public async Task Check_RealRunByRoot_RunsAsTheUserTheHostRunsAs()
        {
            Skip.If(LibC.EffectiveUserId() != 0, "Only root can run something as another user.");
            using (TempFileHelper temp = new TempFileHelper())
            {
                DeployableVersion version = HostVersion(temp, "/usr/bin/id -un; /usr/bin/id -Gn; echo \"$HOME\"; /bin/cat \"$2/plugin.dll\"; : > \"$2/written\" && echo wrote; exit 1");
                string extension = temp.CreateTempDirectory("images");
                string plugin = Path.Combine(extension, "plugin.dll");
                File.WriteAllText(plugin, "readable\n");
                const UnixFileMode onlyRoot = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                foreach (string path in new[] { plugin, extension, version.ExecutablePath, version.Directory, temp.TempDirectory })
                {
                    File.SetUnixFileMode(path, onlyRoot);
                }

                FilePermissionManager permissions = new FilePermissionManager(new MockOutputWriter());
                await permissions.SetExecutablePermissionsAsync(version.ExecutablePath);
                await permissions.SetDirectoryPermissionsAsync(temp.TempDirectory);

                string? refusal = await new ExtensionCheck(TestScope.Create(temp.TempDirectory, isSystem: true))
                    .FindRefusalAsync(new InstalledApp { Name = "shelf", RunAs = "daemon" }, version, extension);

                // Before it, the shell's own words on what it could not write.
                Assert.EndsWith($"daemon / daemon / {LibC.FindUser("daemon")!.Home} / readable", refusal);
                Assert.False(File.Exists(Path.Combine(extension, "written")));
            }
        }

        [Fact]
        public async Task Check_OutputWithoutEnd_IsReadButNotKept()
        {
            ProcessRunner.Result result = await ProcessRunner.RunAsync(
                new ProcessInvocation("/bin/sh", new[] { "-c", "echo abcdef >&2; head -c 1000000 /dev/zero | tr '\\0' x; exit 3" }) { MaxOutput = 4 }, CancellationToken.None);

            Assert.Equal(new ProcessRunner.Result(3, "xxxx", "abcd"), result);
        }

        [Fact]
        public async Task Check_HostWithoutACheck_AcceptsWithoutRunning()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                DeployableVersion version = HostVersion(temp, "exit 1", """{ "executable": "bin/shelf", "command": "shelf", "extensions": { "variable": "P" } }""");

                Assert.Null(await new ExtensionCheck(TestScope.Create(temp.TempDirectory)).FindRefusalAsync(Host, version, "/x"));
            }
        }

        [Fact]
        public async Task Check_HostWithoutExtensions_Refuses()
        {
            using (TempFileHelper temp = new TempFileHelper())
            {
                DeployableVersion version = HostVersion(temp, "exit 0", """{ "executable": "bin/shelf", "command": "shelf" }""");

                Assert.Equal("shelf 1.0.0 takes no extensions", await new ExtensionCheck(TestScope.Create(temp.TempDirectory)).FindRefusalAsync(Host, version, "/x"));
            }
        }
    }
}
