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
        private static async Task<PluginSession> OpenAsync(AppHarness h, StrictClients clients)
        {
            await h.AddPluginAsync("github");
            await h.AddPluginAsync("other");
            return new PluginSessions(clients.Create, h.Secrets, h.Plugins, h.Credentials(), h.Output).Open();
        }

        private static Task<Version?> AskAsync(PluginSession session, string plugin) => session.RunAsync(plugin, client => client.GetLatestVersionAsync("owner/app"));

        [Fact]
        public async Task RunAsync_CallsToTwoPlugins_StartsEachOnceAndStopsBothAtTheEnd()
        {
            using (AppHarness h = new AppHarness())
            {
                StrictClients clients = new StrictClients();
                await using (PluginSession session = await OpenAsync(h, clients))
                {
                    await AskAsync(session, "github");
                    await AskAsync(session, "github");
                    await AskAsync(session, "other");
                    await AskAsync(session, "github");
                    Assert.Equal(new[] { "start github", "start other" }, clients.Calls);
                }

                Assert.Equal(new[] { "start github", "start other", "stop github", "stop other" }, clients.Calls);
            }
        }

        [Fact]
        public async Task RunAsync_PluginDoesNotStart_IsStoppedAndNotTriedAgain()
        {
            using (AppHarness h = new AppHarness())
            {
                StrictClients clients = new StrictClients { OnInitialize = _ => throw new PluginException("bad secrets") };
                await using (PluginSession session = await OpenAsync(h, clients))
                {
                    PluginUnavailableException first = await Assert.ThrowsAsync<PluginUnavailableException>(() => AskAsync(session, "github"));
                    PluginUnavailableException second = await Assert.ThrowsAsync<PluginUnavailableException>(() => AskAsync(session, "github"));
                    clients.OnInitialize = _ => { };
                    await AskAsync(session, "other");

                    Assert.Equal("the plugin 'github' failed to start: bad secrets", first.Message);
                    Assert.Equal(first.Message, second.Message);
                }

                Assert.Equal(new[] { "start github", "stop github", "start other", "stop other" }, clients.Calls);
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

        /// <summary>It is stopped, but not unavailable: nothing is known to be wrong with it.</summary>
        [Fact]
        public async Task RunAsync_CancelledWhileThePluginStarts_IsCancelledAndCanBeCalledAgain()
        {
            using (AppHarness h = new AppHarness())
            {
                StrictClients clients = new StrictClients { OnInitialize = _ => throw new OperationCanceledException() };
                await using (PluginSession session = await OpenAsync(h, clients))
                {
                    await Assert.ThrowsAsync<OperationCanceledException>(() => AskAsync(session, "github"));
                    Assert.Equal(new[] { "start github", "stop github" }, clients.Calls);
                    clients.OnInitialize = _ => { };

                    Assert.Equal(StrictClients.Latest, await AskAsync(session, "github"));
                }
            }
        }

        [Fact]
        public async Task RunAsync_PluginNeedsASecretToStart_AsksForItAndStartsItWithIt()
        {
            using (AppHarness h = new AppHarness())
            {
                StrictClients clients = new StrictClients { OnInitialize = RequireToken };
                h.Prompter.Answers.Enqueue("tok");
                await using (PluginSession session = await OpenAsync(h, clients))
                {
                    Assert.Equal(StrictClients.Latest, await AskAsync(session, "github"));
                    await AskAsync(session, "github");
                }

                Assert.Single(h.Prompter.Asked);
                Assert.Equal(new[] { "start github", "stop github", "start github", "stop github" }, clients.Calls);
            }
        }

        /// <summary>Under the timer: nothing to ask, but the next call, as of the next package, says it again.</summary>
        [Fact]
        public async Task RunAsync_PluginNeedsASecretToStartAndNoOneIsThere_NamesTheCommand()
        {
            using (AppHarness h = new AppHarness())
            {
                StrictClients clients = new StrictClients { OnInitialize = RequireToken };
                h.Prompter.IsInteractive = false;
                await using (PluginSession session = await OpenAsync(h, clients))
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
                StrictClients clients = new StrictClients { OnInitialize = given => secrets = given };
                h.Prompter.Answers.Enqueue("tok");
                await using (PluginSession session = await OpenAsync(h, clients))
                {
                    await session.RunAsync("github", _ =>
                    {
                        RequireToken(secrets);
                        return Task.FromResult(true);
                    });
                }

                Assert.Equal(new[] { "start github", "stop github" }, clients.Calls);
            }
        }

        [Fact]
        public async Task RunAsync_PluginCannotBeStopped_WarnsAndGoesOn()
        {
            using (AppHarness h = new AppHarness())
            {
                StrictClients clients = new StrictClients { StopFailure = new IOException("it hangs") };
                await using (PluginSession session = await OpenAsync(h, clients))
                {
                    await AskAsync(session, "github");
                    await AskAsync(session, "other");
                }

                Assert.Equal(
                    new[] { "Warning: failed to stop the plugin 'github': it hangs", "Warning: failed to stop the plugin 'other': it hangs" },
                    h.Output.Errors);
            }
        }

        /// <summary>Makes the session's clients, and records what is done with them.</summary>
        private sealed class StrictClients
        {
            public static readonly Version Latest = new Version(1, 2, 3);

            /// <summary>The starts and stops, in order.</summary>
            public List<string> Calls { get; } = new List<string>();

            /// <summary>Called with the secrets.</summary>
            public Action<string?> OnInitialize { get; set; } = _ => { };

            public Exception? StopFailure { get; init; }

            public IDistributionServiceClient Create() => new StrictClient(this);
        }

        private sealed class StrictClient : IDistributionServiceClient
        {
            private readonly StrictClients _clients;
            private string? _connected;
            private bool _initialized;

            public StrictClient(StrictClients clients)
            {
                _clients = clients;
            }

            public Task<DistributionServiceInformation> ConnectAsync(string pluginExecutablePath, CancellationToken cancellationToken = default)
            {
                Require(_connected == null, "connected already");
                _connected = Path.GetFileName(pluginExecutablePath);
                _clients.Calls.Add($"start {_connected}");
                return Task.FromResult(new DistributionServiceInformation { FullName = "x", DefaultAlias = "x", Description = "x", Version = "1.0.0" });
            }

            public Task InitializeAsync(string? secrets, CancellationToken cancellationToken = default)
            {
                Require(_connected != null, "not connected");
                _clients.OnInitialize(secrets);
                _initialized = true;
                return Task.CompletedTask;
            }

            public Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
            {
                Require(_initialized, "not initialized");
                return Task.FromResult<Version?>(StrictClients.Latest);
            }

            public Task<DownloadResult> DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public ValueTask DisposeAsync()
            {
                _clients.Calls.Add($"stop {_connected}");
                _connected = null;
                _initialized = false;
                return _clients.StopFailure == null ? ValueTask.CompletedTask : ValueTask.FromException(_clients.StopFailure);
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
