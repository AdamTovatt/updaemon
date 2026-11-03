using Updaemon.Configuration;
using Updaemon.IO;
using Updaemon.Services;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    public class CommandWrapperTests
    {
        [Fact]
        public async Task Wrapper_LoadsSettingsAndReadableSecrets_AndPassesArgumentsThrough()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                (string wrapper, _) = await CreateWrapperAsync(tempHelper);

                string output = await Shell.RunAsync(wrapper, "sync", "two words");

                Assert.Equal("URL=https://x\nTOKEN=s3cret\narg=sync\narg=two words\n", output);
            }
        }

        [Fact]
        public async Task Wrapper_SecretsNotReadable_StillRunsWithSettings()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                (string wrapper, string secrets) = await CreateWrapperAsync(tempHelper);
                // The wrapper tests the file with 'test -r', which is false for a missing file just as for one
                // the caller may not read. A missing file takes that branch for every user, root included.
                File.Delete(secrets);

                string output = await Shell.RunAsync(wrapper);

                Assert.Equal("URL=https://x\nTOKEN=\n", output);
            }
        }

        [Fact]
        public void ReadOwner_RecognizesOnlyUpdaemonWrappers()
        {
            using (TempFileHelper tempHelper = new TempFileHelper())
            {
                string wrapper = tempHelper.CreateTempFile("wrapper", CommandWrapper.Render("byteshelf", "/x", "/s", "/t"));
                string foreign = tempHelper.CreateTempFile("foreign", "#!/bin/sh\necho hi\n");

                Assert.Equal("byteshelf", CommandWrapper.ReadOwner(wrapper));
                Assert.Null(CommandWrapper.ReadOwner(foreign));
                Assert.Null(CommandWrapper.ReadOwner(tempHelper.GetFullPath("missing")));
            }
        }

        private static async Task<(string Wrapper, string Secrets)> CreateWrapperAsync(TempFileHelper tempHelper)
        {
            string executable = tempHelper.CreateTempFile("app dir/app", Shell.PrintEnvironmentScript("URL", "TOKEN"));
            File.SetUnixFileMode(executable, AtomicFile.Executable);
            string settings = tempHelper.GetFullPath("settings.env");
            string secrets = tempHelper.GetFullPath("secrets.env");
            await EnvFile.WriteAsync(settings, new Dictionary<string, string> { ["URL"] = "https://x" }, AtomicFile.Readable);
            await EnvFile.WriteAsync(secrets, new Dictionary<string, string> { ["TOKEN"] = "s3cret" }, AtomicFile.OwnerOnly);

            string wrapper = tempHelper.CreateTempFile("bin/app", CommandWrapper.Render("app", executable, settings, secrets));
            return (wrapper, secrets);
        }
    }
}
