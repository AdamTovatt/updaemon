using Updaemon.Common.Models;
using Updaemon.Common.Utilities;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;

namespace Updaemon.Tests.Services
{
    /// <summary>When plugins are started and stopped, against a client that, like the real one, answers only while connected and initialized.</summary>
    public class PluginSessionTests
    {
        private static async Task<PluginSession> OpenAsync(AppHarness h, StrictClient client)
        {
            await h.AddPluginAsync("github");
            await h.AddPluginAsync("other");
            return new PluginSessions(client, h.Secrets, h.Plugins, h.Credentials(), h.Output).Open();
        }

        private static Task<Version?> AskAsync(PluginSession session, string plugin) => session.RunAsync(plugin, client => client.GetLatestVersionAsync("owner/app"));

        [Fact]
        public async Task RunAsync_CallsToTwoPlugins_StopsOneBeforeTheOtherAndTheLastAtTheEnd()
        {
            using (AppHarness h = new AppHarness())
            {
                StrictClient client = new StrictClient();
                await using (PluginSession session = await OpenAsync(h, client))
                {
                    await AskAsync(session, "github");
                    await AskAsync(session, "github");
                    await AskAsync(session, "other");
                }

                Assert.Equal(new[] { "start github", "stop", "start other", "stop" }, client.Calls);
            }
        }

        [Fact]
        public async Task RunAsync_PluginDoesNotStart_IsStoppedAndNotTriedAgain()
        {
            using (AppHarness h = new AppHarness())
            {
                StrictClient client = new StrictClient { OnInitialize = _ => throw new PluginException("bad secrets") };
                await using (PluginSession session = await OpenAsync(h, client))
                {
                    PluginUnavailableException first = await Assert.ThrowsAsync<PluginUnavailableException>(() => AskAsync(session, "github"));
                    PluginUnavailableException second = await Assert.ThrowsAsync<PluginUnavailableException>(() => AskAsync(session, "github"));
                    client.OnInitialize = _ => { };
                    await AskAsync(session, "other");

                    Assert.Equal("the plugin 'github' failed to start: bad secrets", first.Message);
                    Assert.Equal(first.Message, second.Message);
                }

                Assert.Equal(new[] { "start github", "stop", "start other", "stop" }, client.Calls);
            }
        }

        private static CredentialNeededException NeedsToken() =>
            new CredentialNeededException(new CredentialNeed { Secret = "githubToken", Problem = CredentialProblem.Missing }, "No githubToken.");

        private static void RequireToken(string? secrets)
        {
            if (secrets?.Contains("tok") != true)
            {
                throw NeedsToken();
            }
        }

        [Fact]
        public async Task RunAsync_PluginNeedsASecretToStart_AsksForItAndStartsItWithIt()
        {
            using (AppHarness h = new AppHarness())
            {
                StrictClient client = new StrictClient { OnInitialize = RequireToken };
                h.Prompter.Answers.Enqueue("tok");
                await using (PluginSession session = await OpenAsync(h, client))
                {
                    Assert.Equal(StrictClient.Latest, await AskAsync(session, "github"));
                    await AskAsync(session, "github");
                }

                Assert.Single(h.Prompter.Asked);
                Assert.Equal(new[] { "start github", "stop", "start github", "stop" }, client.Calls);
            }
        }

        /// <summary>Under the timer: nothing to ask, but the next call, as of the next package, says it again.</summary>
        [Fact]
        public async Task RunAsync_PluginNeedsASecretToStartAndNoOneIsThere_NamesTheCommand()
        {
            using (AppHarness h = new AppHarness())
            {
                StrictClient client = new StrictClient { OnInitialize = RequireToken };
                h.Prompter.IsInteractive = false;
                await using (PluginSession session = await OpenAsync(h, client))
                {
                    for (int i = 0; i < 2; i++)
                    {
                        CredentialNeededException ex = await Assert.ThrowsAsync<CredentialNeededException>(() => AskAsync(session, "github"));
                        Assert.Equal("No githubToken. Set it with 'updaemon secret-set github githubToken'.", ex.Message);
                    }
                }
            }
        }

        [Fact]
        public async Task RunAsync_CallNeedsASecret_GivesItToTheRunningPlugin()
        {
            using (AppHarness h = new AppHarness())
            {
                string? secrets = null;
                StrictClient client = new StrictClient { OnInitialize = given => secrets = given };
                h.Prompter.Answers.Enqueue("tok");
                await using (PluginSession session = await OpenAsync(h, client))
                {
                    await session.RunAsync("github", _ =>
                    {
                        RequireToken(secrets);
                        return Task.FromResult(true);
                    });
                }

                Assert.Equal(new[] { "start github", "stop" }, client.Calls);
            }
        }

        [Fact]
        public async Task RunAsync_PluginCannotBeStopped_WarnsAndGoesOn()
        {
            using (AppHarness h = new AppHarness())
            {
                StrictClient client = new StrictClient { StopFailure = new IOException("it hangs") };
                await using (PluginSession session = await OpenAsync(h, client))
                {
                    await AskAsync(session, "github");
                    await AskAsync(session, "other");
                }

                Assert.Equal(
                    new[] { "Warning: failed to stop the plugin 'github': it hangs", "Warning: failed to stop the plugin 'other': it hangs" },
                    h.Output.Errors);
            }
        }

        private sealed class StrictClient : IDistributionServiceClient
        {
            public static readonly Version Latest = new Version(1, 2, 3);

            private bool _connected;
            private bool _initialized;

            /// <summary>The starts and stops, in order.</summary>
            public List<string> Calls { get; } = new List<string>();

            /// <summary>Called with the secrets.</summary>
            public Action<string?> OnInitialize { get; set; } = _ => { };

            public Exception? StopFailure { get; init; }

            public Task<DistributionServiceInformation> ConnectAsync(string pluginExecutablePath, CancellationToken cancellationToken = default)
            {
                Require(!_connected, "connected already");
                _connected = true;
                Calls.Add($"start {Path.GetFileName(pluginExecutablePath)}");
                return Task.FromResult(new DistributionServiceInformation { FullName = "x", DefaultAlias = "x", Description = "x", Version = "1.0.0" });
            }

            public Task InitializeAsync(string? secrets, CancellationToken cancellationToken = default)
            {
                Require(_connected, "not connected");
                OnInitialize(secrets);
                _initialized = true;
                return Task.CompletedTask;
            }

            public Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
            {
                Require(_initialized, "not initialized");
                return Task.FromResult<Version?>(Latest);
            }

            public Task<DownloadResult> DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public ValueTask DisposeAsync()
            {
                Calls.Add("stop");
                _connected = _initialized = false;
                return StopFailure == null ? ValueTask.CompletedTask : ValueTask.FromException(StopFailure);
            }

            private static void Require(bool condition, string problem)
            {
                if (!condition)
                {
                    throw new InvalidOperationException($"The client is {problem}.");
                }
            }
        }
    }
}
