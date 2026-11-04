using Updaemon.Common.Models;
using Updaemon.Common.Utilities;
using Updaemon.Services;
using Updaemon.Tests.Mocks;

namespace Updaemon.Tests.Services
{
    public class PluginCredentialsTests
    {
        private readonly MockSecretsManager _secrets = new MockSecretsManager();
        private readonly MockPrompter _prompter = new MockPrompter();
        private readonly MockOutputWriter _output = new MockOutputWriter();

        private PluginCredentials Credentials() => new PluginCredentials(_secrets, _prompter, _output);

        private static CredentialNeededException Need(CredentialProblem problem, string message) =>
            new CredentialNeededException(new CredentialNeed { Secret = "githubToken", Problem = problem }, message);

        [Theory]
        [InlineData(false, "GitHub's rate limit is used up until 14:05 UTC. A githubToken raises the limit; set one with 'updaemon secret-set github githubToken'.")]
        [InlineData(true, "GitHub's rate limit is used up until 14:05 UTC.")]
        public async Task RunAsync_RateLimited_IsReportedWithoutAsking(bool tokenSet, string expected)
        {
            if (tokenSet)
            {
                await _secrets.SetSecretAsync("github", "githubToken", "tok");
            }

            CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(() => Credentials().RunAsync<bool>(
                "github", () => throw Need(CredentialProblem.RateLimited, "GitHub's rate limit is used up until 14:05 UTC."), null, CancellationToken.None));

            Assert.Equal(expected, ex.Message);
            Assert.Empty(_prompter.Asked);
        }

        [Fact]
        public async Task RunAsync_EmptyAnswer_FailsNamingTheCommand()
        {
            _prompter.Answers.Enqueue("  ");

            CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(() => Credentials().RunAsync<bool>(
                "github", () => throw Need(CredentialProblem.Missing, "owner/app was not found."), null, CancellationToken.None));

            Assert.Equal("No githubToken was given. Set it with 'updaemon secret-set github githubToken'.", ex.Message);
            Assert.Null(await _secrets.GetSecretAsync("github", "githubToken"));
        }

        [Fact]
        public async Task RunAsync_Succeeds_AsksNothing()
        {
            Assert.Equal(7, await Credentials().RunAsync("github", () => Task.FromResult(7), null, CancellationToken.None));
            Assert.Empty(_prompter.Asked);
        }
    }
}
