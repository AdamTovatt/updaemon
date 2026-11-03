using Updaemon.Common.Utilities;
using Updaemon.Models;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Commands
{
    /// <summary>
    /// install, update and dist-install when GitHub needs a token: asked for at a terminal, stored as
    /// the plugin's secret and tried once more; without a terminal, one line naming the command.
    /// </summary>
    public class CredentialPromptTests
    {
        private const string Manifest = """{ "executable": "app", "command": "app" }""";

        private const string NotFound = "owner/private was not found on GitHub: it does not exist, or it is private and needs a githubToken.";

        private static async Task<AppHarness> PrivateRepositoryAsync(string version = "1.0.0")
        {
            AppHarness h = new AppHarness();
            await h.AddPluginAsync();
            h.AddRelease("owner/private", version, Manifest, executable: "app");
            h.Client.TokenProtectedRemotes["owner/private"] = "tok";
            return h;
        }

        private static Task<int> InstallAsync(AppHarness h) =>
            h.Install().ExecuteAsync(new[] { "private", "--from", "github:owner/private" });

        [Fact]
        public async Task Install_PrivateRepositoryAtATerminal_AsksForTheTokenAndTriesAgain()
        {
            using (AppHarness h = await PrivateRepositoryAsync())
            {
                h.Prompter.Answers.Enqueue("tok");

                int exitCode = await InstallAsync(h);

                Assert.Equal(0, exitCode);
                Assert.Equal(new[] { NotFound }, h.Output.Errors);
                Assert.Equal(new[] { ("githubToken for the 'github' plugin (not shown): ", true) }, h.Prompter.Asked);
                Assert.Equal("tok", await h.Secrets.GetSecretAsync("github", "githubToken"));
                Assert.NotNull(await h.Config.GetAppAsync("private"));
            }
        }

        [Fact]
        public async Task Install_TokenRejectedAtATerminal_AsksForANewOne()
        {
            using (AppHarness h = await PrivateRepositoryAsync())
            {
                await h.Secrets.SetSecretAsync("github", "githubToken", "old");
                h.Prompter.Answers.Enqueue("tok");

                int exitCode = await InstallAsync(h);

                Assert.Equal(0, exitCode);
                Assert.Equal(new[] { ("A new githubToken for the 'github' plugin (not shown): ", true) }, h.Prompter.Asked);
                Assert.Equal("tok", await h.Secrets.GetSecretAsync("github", "githubToken"));
            }
        }

        [Fact]
        public async Task Install_AnswerRefusedToo_IsReportedWithoutAskingAgain()
        {
            using (AppHarness h = await PrivateRepositoryAsync())
            {
                h.Prompter.Answers.Enqueue("wrong");

                int exitCode = await InstallAsync(h);

                Assert.Equal(1, exitCode);
                Assert.Single(h.Prompter.Asked);
                Assert.Equal(
                    "Error: private: GitHub rejected the githubToken: it is wrong, expired or revoked. Set it with 'updaemon secret-set github githubToken'.",
                    h.Output.Errors[^1]);
                Assert.Null(await h.Config.GetAppAsync("private"));
            }
        }

        [Fact]
        public async Task Install_NoTerminal_FailsInOneLineNamingTheCommand()
        {
            using (AppHarness h = await PrivateRepositoryAsync())
            {
                h.Prompter.IsInteractive = false;

                int exitCode = await InstallAsync(h);

                Assert.Equal(1, exitCode);
                Assert.Equal(new[] { $"Error: private: {NotFound} Set it with 'updaemon secret-set github githubToken'." }, h.Output.Errors);
                Assert.Empty(h.Prompter.Asked);
            }
        }

        [Fact]
        public async Task Update_TwoPrivateAppsAtATerminal_AsksOnceAndTheRunningPluginUsesTheTokenForBoth()
        {
            using (AppHarness h = await PrivateRepositoryAsync())
            {
                h.AddRelease("owner/private2", "1.0.0", """{ "executable": "app", "command": "app2" }""", executable: "app");
                h.Client.TokenProtectedRemotes["owner/private2"] = "tok";
                await h.Secrets.SetSecretAsync("github", "githubToken", "tok");
                Assert.Equal(0, await InstallAsync(h));
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "private2", "--from", "github:owner/private2" }));
                await h.Secrets.RemoveSecretAsync("github", "githubToken");
                h.AddRelease("owner/private", "1.1.0", Manifest, executable: "app");
                h.AddRelease("owner/private2", "1.1.0", """{ "executable": "app", "command": "app2" }""", executable: "app");
                h.Prompter.Answers.Enqueue("tok");

                int exitCode = await h.Update().ExecuteAsync(Array.Empty<string>());

                Assert.Equal(0, exitCode);
                Assert.Single(h.Prompter.Asked);
                Assert.Equal(new Version(1, 1, 0), await h.Deployer.GetCurrentVersionAsync("private"));
                Assert.Equal(new Version(1, 1, 0), await h.Deployer.GetCurrentVersionAsync("private2"));
            }
        }

        [Fact]
        public async Task Update_NoTerminal_FailsInOneLineNamingTheCommand()
        {
            using (AppHarness h = await PrivateRepositoryAsync())
            {
                await h.Secrets.SetSecretAsync("github", "githubToken", "tok");
                Assert.Equal(0, await InstallAsync(h));
                await h.Secrets.RemoveSecretAsync("github", "githubToken");
                h.Prompter.IsInteractive = false;

                int exitCode = await h.Update().ExecuteAsync(new[] { "private" });

                Assert.Equal(1, exitCode);
                Assert.Equal(new[] { $"Error: private: {NotFound} Set it with 'updaemon secret-set github githubToken'." }, h.Output.Errors);
            }
        }

        [Fact]
        public async Task DistInstall_PrivatePluginRepositoryAtATerminal_StoresTheTokenAsTheGitHubPluginsAndTriesAgain()
        {
            using (AppHarness h = new AppHarness())
            {
                h.GitHubPlugins.LatestTags["someone/private-plugin"] = "v1.0.0";
                h.GitHubPlugins.NextFailures.Enqueue(GitHubAccess.RepositoryNotFound("someone", "private-plugin", hasToken: false));
                h.Prompter.Answers.Enqueue("tok");

                int exitCode = await h.DistInstall().ExecuteAsync(new[] { "github:someone/private-plugin" });

                Assert.Equal(0, exitCode);
                Assert.Equal("tok", await h.Secrets.GetSecretAsync("github", "githubToken"));
                Assert.IsType<PluginSource.GitHub>((await h.Config.GetPluginAsync("mock"))!.Source);
            }
        }

        [Fact]
        public async Task DistInstall_PrivatePluginRepositoryWithoutATerminal_FailsInOneLineNamingTheCommand()
        {
            using (AppHarness h = new AppHarness())
            {
                h.GitHubPlugins.NextFailures.Enqueue(GitHubAccess.RepositoryNotFound("someone", "private-plugin", hasToken: false));
                h.Prompter.IsInteractive = false;

                int exitCode = await h.DistInstall().ExecuteAsync(new[] { "github:someone/private-plugin" });

                Assert.Equal(1, exitCode);
                Assert.Equal(
                    new[] { "Error: someone/private-plugin was not found on GitHub: it does not exist, or it is private and needs a githubToken. Set it with 'updaemon secret-set github githubToken'." },
                    h.Output.Errors);
                Assert.Null(await h.Config.GetPluginAsync("mock"));
            }
        }
    }
}
