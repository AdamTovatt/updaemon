using Updaemon.Configuration;
using Updaemon.IO;
using Updaemon.Services;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    public class AppWrapperTests
    {
        public delegate string Renderer(string appName, string executablePath, string settingsFilePath, string secretsFilePath);

        public static TheoryData<Renderer> BothWrappers => new TheoryData<Renderer> { AppWrapper.ForCommand, AppWrapper.ForService };

        [Theory]
        [MemberData(nameof(BothWrappers))]
        public async Task Wrapper_LoadsSettingsAndTheCallersOwnSecrets_AndPassesArgumentsThrough(Renderer render)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                (string wrapper, _) = await CreateWrapperAsync(tempHelper, render);

                string output = await Shell.RunAsync(wrapper, "sync", "two words");

                Assert.Equal("URL=https://x\nTOKEN=s3cret\narg=sync\narg=two words\n", output);
            }
        }

        [Theory]
        [MemberData(nameof(BothWrappers))]
        public async Task Wrapper_NoSecretsFile_RunsWithSettings(Renderer render)
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                (string wrapper, string secrets) = await CreateWrapperAsync(tempHelper, render);
                File.Delete(secrets);

                string output = await Shell.RunAsync(wrapper);

                Assert.Equal("URL=https://x\nTOKEN=\n", output);
            }
        }

        [SkippableFact]
        public async Task ForCommand_SecretsSomeoneElseOwns_AreNotRun()
        {
            Skip.If(LibC.EffectiveUserId() == 0, "Root owns the only other-owned file to hand, so nothing here is someone else's.");
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                (string wrapper, string secrets) = await CreateWrapperAsync(tempHelper, AppWrapper.ForCommand);
                // A file root owns and anyone may read: run as shell lines, each of its lines ('127.0.0.1 localhost')
                // would be a command the shell cannot find, and says so on stderr.
                File.Delete(secrets);
                File.CreateSymbolicLink(secrets, "/etc/hosts");

                (int exitCode, string output, string error) = await Shell.RunForResultAsync(wrapper);

                Assert.Equal(string.Empty, error);
                Assert.Equal(0, exitCode);
                Assert.Equal("URL=https://x\nTOKEN=\n", output);
            }
        }

        [SkippableFact]
        public async Task ServiceWrapper_SecretsNotReadable_FailsInsteadOfRunningWithoutThem()
        {
            Skip.If(LibC.EffectiveUserId() == 0, "Root reads a file of any mode.");
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                (string wrapper, string secrets) = await CreateWrapperAsync(tempHelper, AppWrapper.ForService);
                File.SetUnixFileMode(secrets, UnixFileMode.None);

                (int exitCode, string output, _) = await Shell.RunForResultAsync(wrapper);

                Assert.NotEqual(0, exitCode);
                Assert.Equal(string.Empty, output);
            }
        }

        [Fact]
        public void ReadOwner_RecognizesOnlyUpdaemonWrappers()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string wrapper = tempHelper.CreateTempFile("wrapper", AppWrapper.ForCommand("byteshelf", "/x", "/s", "/t"));
                string foreign = tempHelper.CreateTempFile("foreign", "#!/bin/sh\necho hi\n");

                Assert.Equal("byteshelf", AppWrapper.ReadOwner(wrapper));
                Assert.Null(AppWrapper.ReadOwner(foreign));
                Assert.Null(AppWrapper.ReadOwner(tempHelper.GetFullPath("missing")));
            }
        }

        private static async Task<(string Wrapper, string Secrets)> CreateWrapperAsync(TempFileHelper tempHelper, Renderer render)
        {
            string executable = tempHelper.CreateTempFile("app dir/app", Shell.PrintEnvironmentScript("URL", "TOKEN"));
            File.SetUnixFileMode(executable, AtomicFile.Executable);
            string settings = tempHelper.GetFullPath("settings.env");
            string secrets = tempHelper.GetFullPath("secrets.env");
            await EnvFile.WriteAsync(settings, new Dictionary<string, string> { ["URL"] = "https://x" }, AtomicFile.Readable);
            await EnvFile.WriteAsync(secrets, new Dictionary<string, string> { ["TOKEN"] = "s3cret" }, AtomicFile.OwnerOnly);

            string wrapper = tempHelper.CreateTempFile("bin/app", render("app", executable, settings, secrets));
            return (wrapper, secrets);
        }
    }
}
