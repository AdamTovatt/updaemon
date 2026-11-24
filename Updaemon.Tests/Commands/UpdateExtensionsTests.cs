using Updaemon.Common.Models;
using Updaemon.Common.Utilities;
using Updaemon.Interfaces;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;
using static Updaemon.Tests.Helpers.ShelfPackages;

namespace Updaemon.Tests.Commands
{
    /// <summary>update of a host together with its extensions, and of an extension by itself.</summary>
    public class UpdateExtensionsTests
    {
        private static readonly ProcessRunner.Result Accepted = new ProcessRunner.Result(0, string.Empty, string.Empty);

        private static Task<int> UpdateAsync(AppHarness h, string? name = null) => h.Update().ExecuteAsync(name == null ? Array.Empty<string>() : new[] { name });

        /// <summary>The host and its extension images at 1.0.0, with 2.0.0 of what is asked for released.</summary>
        private static async Task<AppHarness> InstalledAsync(bool newHost = false, bool newImages = false, bool isSystem = false, string hostManifest = AppHarness.HostManifest)
        {
            AppHarness h = new AppHarness(isSystem);
            await h.InstallHostWithImagesAsync();
            if (newHost)
            {
                h.AddHostRelease("2.0.0", hostManifest);
            }

            if (newImages)
            {
                h.AddExtensionRelease(ImagesRemote, "2.0.0");
            }

            return h;
        }

        private static async Task AssertCurrentAsync(AppHarness h, Version host, string images)
        {
            Assert.Equal(host, await h.Versions.GetCurrentAsync(Shelf));
            Assert.Contains($"images -> {images}", h.LinkedExtensions());
            Assert.Null((await h.HostAsync()).Unfinished);
        }

        /// <summary>What a host was asked: its version and the extension's.</summary>
        private static (string Host, string Extension) Asked(ProcessInvocation check) =>
            (Path.GetFileName(check.WorkingDirectory)!, Path.GetFileName(check.Arguments[^1]));

        private static IEnumerable<(string Host, string Extension)> Asked(AppHarness h) => h.Checks.Select(Asked);

        /// <summary>Refuses these pairs of host and extension version.</summary>
        private static void Refuse(AppHarness h, params (string Host, string Extension)[] pairs) =>
            h.Refuse("needs another shelf", check => pairs.Contains(Asked(check)));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Update_HostAndExtensionsFromDifferentPlugins_SwitchesAllWithOneRestart(bool isSystem)
        {
            using (AppHarness h = new AppHarness(isSystem))
            {
                await h.AddPluginAsync("other");
                await h.InstallHostWithImagesAsync();
                h.AddExtensionRelease("audio", "1.0.0");
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/audio" }));
                h.AddHostRelease("2.0.0");
                h.AddExtensionRelease(ImagesRemote, "2.0.0");
                h.AddExtensionRelease("audio", "3.0.0");
                IReadOnlyList<string>? linkedOnceDownloaded = null;
                h.AfterDownloading("audio", "3.0.0", _ => linkedOnceDownloaded = h.LinkedExtensions());
                IReadOnlyList<string>? linkedAtRestart = null;
                h.ServiceManager.OnRestart = () => linkedAtRestart = h.LinkedExtensions();
                h.ForgetCalls();

                Assert.Equal(0, await UpdateAsync(h));

                Assert.Empty(h.Output.Errors);
                Assert.Equal(new[] { "audio -> 1.0.0", "images -> 1.0.0" }, linkedOnceDownloaded);
                Assert.Equal(new[] { "audio -> 3.0.0", "images -> 2.0.0" }, linkedAtRestart);
                Assert.Equal(1, h.HostRestarts());
                Assert.Equal(new[] { ("2.0.0", "2.0.0"), ("2.0.0", "3.0.0") }, Asked(h));
                Assert.All(h.Checks, check => Assert.Equal(isSystem ? "daemon" : null, check.User));
                await AssertCurrentAsync(h, Two, "2.0.0");
                Assert.Equal(
                    new[] { "Updated shelf from 1.0.0 to 2.0.0.", "Updated shelf/images from 1.0.0 to 2.0.0.", "Updated shelf/audio from 1.0.0 to 3.0.0." },
                    h.Output.Messages.Where(message => message.StartsWith("Updated ")));
            }
        }

        [Fact]
        public async Task Update_HostDoesNotStayUp_PutsBothBackAndSkipsBoth()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            {
                h.ServiceManager.CrashOnNextStart.Add("shelf");

                Assert.Equal(1, await UpdateAsync(h));

                Assert.Equal(
                    new[] { "Error: shelf, shelf/images: shelf did not stay running with shelf 2.0.0, shelf/images 2.0.0, so shelf 1.0.0, shelf/images 1.0.0 run again and each of the new versions is skipped. See 'updaemon logs shelf'." },
                    h.Output.Errors);
                Assert.DoesNotContain(h.Output.Messages, message => message.StartsWith("Updated "));
                await AssertCurrentAsync(h, One, "1.0.0");
                Assert.Equal(2, h.HostRestarts());
                InstalledApp host = await h.HostAsync();
                Assert.Equal(new[] { Two }, host.SkippedVersions);
                Assert.Equal(new[] { Two }, host.FindExtension("images")!.SkippedVersions);
                Assert.Equal(new[] { "shelf", "shelf/images" }, (await h.RunLog.LoadAsync())!.Failures.Select(failure => failure.Subject));
                // Kept, to go to by name.
                Assert.Equal(new[] { One, Two }, h.Versions.GetRetained(Shelf));
                Assert.Equal(new[] { One, Two }, h.Versions.GetRetained(Images));
            }
        }

        [Fact]
        public async Task Update_AfterBothWereSkipped_OffersNeither()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            {
                h.ServiceManager.CrashOnNextStart.Add("shelf");
                await UpdateAsync(h);
                h.Output.Clear();

                Assert.Equal(0, await UpdateAsync(h));

                Assert.Contains("shelf: 2.0.0 is skipped (it failed its health check or was rolled back from); 1.0.0 stays until a newer version is released.", h.Output.Messages);
                Assert.Contains("shelf/images: 2.0.0 is skipped (it failed its health check or was rolled back from); 1.0.0 stays until a newer version is released.", h.Output.Messages);
                await AssertCurrentAsync(h, One, "1.0.0");
            }
        }

        [Theory]
        [InlineData(true, "1.0.0", "2.0.0")]
        [InlineData(false, "2.0.0", "1.0.0")]
        public async Task Update_OneOfTheTwoWasSkipped_UpdatesTheOther(bool hostWasSkipped, string host, string images)
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            {
                InstalledApp shelf = await h.HostAsync();
                InstalledPackage skips = hostWasSkipped ? shelf : shelf.FindExtension("images")!;
                skips.RecordSwitch(from: Two, to: One);
                await h.Config.SaveAppAsync(shelf);

                Assert.Equal(0, await UpdateAsync(h));

                await AssertCurrentAsync(h, Version.Parse(host), images);
            }
        }

        [Fact]
        public async Task Update_OnlyTheExtensionIsNew_AsksTheCurrentHostAndRestartsIt()
        {
            using (AppHarness h = await InstalledAsync(newImages: true))
            {
                Assert.Equal(0, await UpdateAsync(h));

                Assert.Contains("shelf is up to date (1.0.0).", h.Output.Messages);
                Assert.Equal(new[] { ("1.0.0", "2.0.0") }, Asked(h));
                await AssertCurrentAsync(h, One, "2.0.0");
                Assert.Equal(1, h.HostRestarts());
            }
        }

        [Fact]
        public async Task Update_CurrentHostRefusesTheNewExtension_KeepsTheOldAndTriesAgainNextRun()
        {
            using (AppHarness h = await InstalledAsync(newImages: true))
            {
                Refuse(h, ("1.0.0", "2.0.0"));
                IReadOnlyList<string> before = h.Snapshot();

                Assert.Equal(1, await UpdateAsync(h, "shelf"));

                Assert.Equal(new[] { "Error: shelf/images: shelf 1.0.0 refuses 2.0.0: needs another shelf. 1.0.0 stays." }, h.Output.Errors);
                Assert.Equal(new[] { ("1.0.0", "2.0.0"), ("1.0.0", "1.0.0") }, Asked(h));
                Assert.Equal(0, h.HostRestarts());
                h.AssertUnchangedSince(before);

                h.AnswerCheck = _ => Accepted;
                Assert.Equal(0, await UpdateAsync(h, "shelf"));
                await AssertCurrentAsync(h, One, "2.0.0");
            }
        }

        [Fact]
        public async Task Update_NewHostRefusesTheNewExtensionButTakesTheCurrent_UpdatesOnlyTheHost()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            {
                Refuse(h, ("2.0.0", "2.0.0"));

                Assert.Equal(1, await UpdateAsync(h));

                Assert.Equal(new[] { "Error: shelf/images: shelf 2.0.0 refuses 2.0.0: needs another shelf. 1.0.0 stays." }, h.Output.Errors);
                Assert.Equal(new[] { ("2.0.0", "2.0.0"), ("2.0.0", "1.0.0") }, Asked(h));
                await AssertCurrentAsync(h, Two, "1.0.0");
                Assert.Equal(new[] { One }, h.Versions.GetRetained(Images));
                Assert.Empty((await h.HostAsync()).FindExtension("images")!.SkippedVersions);
            }
        }

        [Fact]
        public async Task Update_NewHostRefusesBothOfTheExtension_UpdatesTheHostAndLeavesItLinked()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            {
                Refuse(h, ("2.0.0", "2.0.0"), ("2.0.0", "1.0.0"));

                Assert.Equal(1, await UpdateAsync(h));

                Assert.Equal(
                    new[] { "Error: shelf/images: shelf 2.0.0 refuses 2.0.0: needs another shelf. It refuses 1.0.0 too, which stays: needs another shelf. Until a newer release of either fixes that, or 'updaemon remove shelf/images' removes it, shelf runs without it." },
                    h.Output.Errors);
                await AssertCurrentAsync(h, Two, "1.0.0");
                Assert.Equal(1, h.HostRestarts());
            }
        }

        /// <summary>So what the host leaves out shows in status until it is fixed or removed, not only once.</summary>
        [Fact]
        public async Task Update_NothingNewButTheHostRefusesTheExtension_FailsEveryRun()
        {
            using (AppHarness h = await InstalledAsync())
            {
                Refuse(h, ("1.0.0", "1.0.0"));
                const string failure = "shelf 1.0.0 refuses 1.0.0: needs another shelf. Until a newer release of either fixes that, or 'updaemon remove shelf/images' removes it, shelf runs without it.";

                Assert.Equal(1, await UpdateAsync(h));
                Assert.Equal(1, await UpdateAsync(h));

                Assert.Equal(new[] { "Error: shelf/images: " + failure, "Error: shelf/images: " + failure }, h.Output.Errors);
                Assert.Equal(new UpdateFailure("shelf/images", failure), Assert.Single((await h.RunLog.LoadAsync())!.Failures));
                Assert.Equal(0, h.HostRestarts());
            }
        }

        private static readonly string NeedsASetting = Changed(AppHarness.HostManifest, ("\"extensions\"", "\"settings\": { \"SHELF_KEY\": { \"required\": true } }, \"extensions\""));

        [Fact]
        public async Task Update_HostCannotBeUpdated_AsksTheCurrentOneAndUpdatesTheExtension()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true, hostManifest: NeedsASetting))
            {
                Assert.Equal(1, await UpdateAsync(h));

                Assert.Equal(new[] { "Error: shelf: 2.0.0 needs SHELF_KEY; 1.0.0 keeps running. Run 'updaemon set shelf SHELF_KEY=<value>', then 'updaemon update shelf'." }, h.Output.Errors);
                Assert.Equal(new[] { ("1.0.0", "2.0.0") }, Asked(h));
                await AssertCurrentAsync(h, One, "2.0.0");
                Assert.Equal(new[] { One }, h.Versions.GetRetained(Shelf));
            }
        }

        [Fact]
        public async Task Update_HostCannotBeDownloaded_UpdatesTheExtension()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            {
                h.AfterDownloading("owner/shelf", "2.0.0", _ => throw new IOException("checksum mismatch"));

                Assert.Equal(1, await UpdateAsync(h));

                Assert.Equal(new[] { "Error: shelf: checksum mismatch" }, h.Output.Errors);
                await AssertCurrentAsync(h, One, "2.0.0");
            }
        }

        [Fact]
        public async Task Update_OneExtensionCannotBeDownloaded_UpdatesTheRest()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            {
                h.AddExtensionRelease("me/video", "1.0.0");
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/video", "--from", "github:me/video" }));
                h.ForgetCalls();
                h.AfterDownloading(ImagesRemote, "2.0.0", _ => throw new IOException("checksum mismatch"));
                h.AddExtensionRelease("me/video", "2.0.0");

                Assert.Equal(1, await UpdateAsync(h));

                Assert.Equal(new[] { "Error: shelf/images: checksum mismatch" }, h.Output.Errors);
                // The one that stays is asked about all the same.
                Assert.Equal(new[] { ("2.0.0", "1.0.0"), ("2.0.0", "2.0.0") }, Asked(h));
                await AssertCurrentAsync(h, Two, "1.0.0");
                Assert.Equal(new[] { "images -> 1.0.0", "video -> 2.0.0" }, h.LinkedExtensions());
                Assert.Equal(1, h.HostRestarts());
            }
        }

        [Fact]
        public async Task Update_ExtensionsPluginIsGone_ReportsItAndUpdatesTheHost()
        {
            using (AppHarness h = await InstalledAsync(newHost: true))
            {
                await h.AddPluginAsync("other");
                h.AddExtensionRelease("audio", "1.0.0");
                h.AddExtensionRelease("video", "1.0.0");
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/audio" }));
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/video", "--from", "other:video" }));
                await h.Config.RemovePluginAsync("other");

                Assert.Equal(1, await UpdateAsync(h));

                // One line for all that comes from it.
                Assert.StartsWith("Error: shelf/audio, shelf/video: the plugin 'other' is not installed", Assert.Single(h.Output.Errors));
                await AssertCurrentAsync(h, Two, "1.0.0");
            }
        }

        /// <summary>An unattended run does not wait for it once per package.</summary>
        [Fact]
        public async Task Update_ExtensionsPluginDoesNotStart_IsTriedOnce()
        {
            using (AppHarness h = await InstalledAsync(newHost: true))
            {
                await h.AddPluginAsync("other");
                h.AddExtensionRelease("audio", "1.0.0");
                h.AddExtensionRelease("video", "1.0.0");
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/audio" }));
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/video", "--from", "other:video" }));
                string path = (await h.Config.GetPluginAsync("other"))!.Path;
                h.Client.ConnectAsyncThrowsForPluginPaths.Add(path);
                h.Client.MethodCalls.Clear();

                Assert.Equal(1, await UpdateAsync(h));

                Assert.StartsWith("Error: shelf/audio, shelf/video: the plugin 'other' failed to start", Assert.Single(h.Output.Errors));
                Assert.Single(h.Client.MethodCalls, $"{nameof(IDistributionServiceClient.ConnectAsync)}:{path}");
                await AssertCurrentAsync(h, Two, "1.0.0");
            }
        }

        /// <summary>It runs until the run is over, so nothing that comes after another plugin's needs it to start again.</summary>
        [Fact]
        public async Task Update_PluginThatWouldNotStartAgain_IsNotStartedAgain()
        {
            using (AppHarness h = await InstalledAsync(newHost: true))
            {
                await h.AddPluginAsync("other");
                h.AddExtensionRelease("audio", "1.0.0");
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/audio" }));
                foreach (string name in new[] { "tool", "zoo" })
                {
                    await InstallToolAsync(h, name, "github");
                }

                h.AddExtensionRelease("audio", "2.0.0");
                string github = (await h.Config.GetPluginAsync("github"))!.Path;
                h.AfterDownloading("audio", "2.0.0", _ => h.Client.ConnectAsyncThrowsForPluginPaths.Add(github));
                h.ForgetCalls();

                Assert.Equal(0, await UpdateAsync(h));

                Assert.Contains(github, h.Client.ConnectAsyncThrowsForPluginPaths);
                Assert.Equal(Two, await h.Versions.GetCurrentAsync(new ExtensionName("shelf", "audio")));
                Assert.Single(h.Client.MethodCalls, $"{nameof(IDistributionServiceClient.ConnectAsync)}:{github}");
                foreach (string name in new[] { "shelf", "tool", "zoo" })
                {
                    Assert.Equal(Two, await h.Versions.GetCurrentAsync(new AppName(name)));
                }
            }
        }

        /// <summary>One that could not be asked for want of a secret is reported for that alone.</summary>
        [Fact]
        public async Task Update_PluginNeedsASecretAndThenDoesNotStart_ReportsEachPackageOnce()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync();
                await InstallToolAsync(h, "tool", "github");
                await InstallToolAsync(h, "zoo", "github");
                string github = (await h.Config.GetPluginAsync("github"))!.Path;
                h.Prompter.IsInteractive = false;
                h.Client.OnInitialize = () =>
                {
                    h.Client.ConnectAsyncThrowsForPluginPaths.Add(github);
                    throw new CredentialNeededException(new CredentialNeed { Secret = "githubToken", Problem = CredentialProblem.Missing }, "No githubToken.");
                };
                h.ForgetCalls();

                Assert.Equal(1, await UpdateAsync(h));

                Assert.Equal(
                    new[]
                    {
                        "Error: tool: No githubToken. Set it with 'updaemon secret-set github githubToken'.",
                        $"Error: zoo: the plugin 'github' failed to start: Mock failure connecting to '{github}'",
                    },
                    h.Output.Errors);
            }
        }

        private static async Task InstallToolAsync(AppHarness h, string name, string plugin)
        {
            string manifest = """{ "executable": "bin/tool", "command": "NAME" }""".Replace("NAME", name);
            h.AddRelease(name, "1.0.0", manifest, "bin/tool");
            Assert.Equal(0, await h.Install().ExecuteAsync(new[] { name, "--from", $"{plugin}:{name}" }));
            h.AddRelease(name, "2.0.0", manifest, "bin/tool");
        }

        /// <summary>The host's version that stays cannot be asked about its extensions.</summary>
        [Theory]
        [InlineData(null, "shelf")]
        [InlineData("shelf/images", "shelf/images")]
        public async Task Update_TheHostsCurrentManifestIsInvalid_FailsWhatWasToBeUpdated(string? named, string subject)
        {
            using (AppHarness h = await InstalledAsync(newImages: true))
            {
                File.WriteAllText(Path.Combine(h.Versions.GetVersionDirectory(Shelf, One), AppManifest.FileName), "{");

                Assert.Equal(1, await UpdateAsync(h, named));

                Assert.StartsWith($"Error: {subject}: Invalid updaemon.json: ", Assert.Single(h.Output.Errors));
                Assert.Equal(new[] { "images -> 1.0.0" }, h.LinkedExtensions());
                Assert.Equal(new[] { One }, h.Versions.GetRetained(Images));
            }
        }

        [Theory]
        [InlineData(null, "shelf")]
        [InlineData("shelf/images", "shelf/images")]
        public async Task Update_HostWithoutACurrentVersion_IsReportedOnce(string? named, string subject)
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            {
                h.Versions.Unlink(Shelf);

                Assert.Equal(1, await UpdateAsync(h, named));

                Assert.Equal(new[] { $"Error: {subject}: shelf has no current version. Remove it with 'updaemon remove shelf', then install it again." }, h.Output.Errors);
                Assert.Equal(new[] { "images -> 1.0.0" }, h.LinkedExtensions());
            }
        }

        [Fact]
        public async Task Update_OneExtensionAndTheServiceManagerIsUnreachable_ChangesNothing()
        {
            using (AppHarness h = await InstalledAsync(newImages: true))
            {
                h.ServiceManager.Unreachable = "no user manager";
                IReadOnlyList<string> before = h.Snapshot();

                Assert.Equal(1, await UpdateAsync(h, "shelf/images"));

                Assert.Equal(new[] { "Error: shelf/images: no user manager" }, h.Output.Errors);
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Update_TheCheckItselfFails_FailsThatExtensionOnly()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            {
                h.AnswerCheck = _ => throw new IOException("no pipe");

                Assert.Equal(1, await UpdateAsync(h));

                Assert.Equal(new[] { "Error: shelf/images: no pipe" }, h.Output.Errors);
                await AssertCurrentAsync(h, Two, "1.0.0");
            }
        }

        /// <summary>Its every generated file differs from 1.0.0's.</summary>
        [Fact]
        public async Task Update_TheSwitchFails_PutsAllBackSkipsNothingAndTriesAgainNextRun()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true, hostManifest: ChangedManifest))
            {
                string taken = h.Scope.GetCommandPath("shelf2");
                File.WriteAllText(taken, "someone else's");
                IReadOnlyList<string> before = h.Snapshot();

                Assert.Equal(1, await UpdateAsync(h, "shelf"));

                Assert.Equal(new[] { $"Error: shelf, shelf/images: '{taken}' already exists and updaemon did not create it. Remove it, then try again." }, h.Output.Errors);
                Assert.Equal(0, h.HostRestarts());
                h.AssertUnchangedSince(before);

                File.Delete(taken);
                Assert.Equal(0, await UpdateAsync(h, "shelf"));
                await AssertCurrentAsync(h, Two, "2.0.0");
            }
        }

        /// <summary>The host is asked whether it can take over before the extensions' checks, and again by the switch: in between, that can change.</summary>
        [Fact]
        public Task Update_HostLosesItsSecretDuringTheCheck_SwitchesNothingSkipsNothingAndTriesAgainNextRun() =>
            AssertNothingSwitchedWhenAsync(
                h => h.Secrets.DeleteAppSecrets("shelf"),
                "2.0.0 needs TOKEN; 1.0.0 keeps running. Run 'updaemon set shelf TOKEN=<value>', then 'updaemon update shelf'.");

        [Fact]
        public Task Update_ServiceManagerBecomesUnreachableDuringTheCheck_SwitchesNothingSkipsNothingAndTriesAgainNextRun() =>
            AssertNothingSwitchedWhenAsync(h => h.ServiceManager.Unreachable = "no user manager", "no user manager");

        private static async Task AssertNothingSwitchedWhenAsync(Action<AppHarness> duringTheCheck, string problem)
        {
            string manifest = Changed(AppHarness.HostManifest, ("\"command\"", "\"settings\": { \"TOKEN\": { \"required\": true, \"secret\": true } }, \"command\""));
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync(manifest, "--set", "TOKEN=abc");
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/images" }));
                h.AddHostRelease("2.0.0", manifest);
                h.AddExtensionRelease(ImagesRemote, "2.0.0");
                h.ForgetCalls();
                h.AnswerCheck = _ =>
                {
                    duringTheCheck(h);
                    return Accepted;
                };

                Assert.Equal(1, await UpdateAsync(h, "shelf"));

                Assert.Equal(new[] { $"Error: shelf, shelf/images: {problem}" }, h.Output.Errors);
                await AssertCurrentAsync(h, One, "1.0.0");
                Assert.Equal(0, h.HostRestarts());
                Assert.Equal(new[] { One }, h.Versions.GetRetained(Shelf));
                Assert.Equal(new[] { One }, h.Versions.GetRetained(Images));
                InstalledApp host = await h.HostAsync();
                Assert.Empty(host.SkippedVersions);
                Assert.Empty(host.FindExtension("images")!.SkippedVersions);

                h.AnswerCheck = _ => Accepted;
                h.ServiceManager.Unreachable = null;
                Assert.Equal(0, await h.Set().ExecuteAsync(new[] { "shelf", "TOKEN=abc" }));
                Assert.Equal(0, await UpdateAsync(h, "shelf"));
                await AssertCurrentAsync(h, Two, "2.0.0");
            }
        }

        [Fact]
        public async Task Update_CannotBeRecorded_PutsBothBackAndRestartsWhatRan()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true, hostManifest: ChangedManifest))
            {
                IReadOnlyList<string> before = h.Snapshot();
                h.SavedConfig.FailNextWithNothingLeft();

                Assert.Equal(1, await UpdateAsync(h, "shelf"));

                Assert.Equal(new[] { "Error: shelf, shelf/images: disk full" }, h.Output.Errors);
                Assert.Equal(2, h.HostRestarts());
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Update_CancelledDuringTheHealthCheck_PutsBothBackAndSkipsNothing()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true, hostManifest: ChangedManifest))
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                IReadOnlyList<string> before = h.Snapshot();
                h.ServiceManager.OnRestart = cancellation.Cancel;

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Update().ExecuteAsync(new[] { "shelf" }, cancellation.Token));

                Assert.Equal(2, h.HostRestarts());
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Update_CancelledDuringAnExtensionsCheck_LeavesNothingDownloaded()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                IReadOnlyList<string> before = h.Snapshot();
                h.AnswerCheck = _ =>
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                };

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Update().ExecuteAsync(new[] { "shelf" }, cancellation.Token));

                h.AssertUnchangedSince(before);
                Assert.Empty(h.Output.Errors);
            }
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(true, 1)]
        public async Task Update_DisabledService_IsRestartedOnlyForTheHost(bool newHost, int restarts)
        {
            using (AppHarness h = await InstalledAsync(newHost, newImages: true))
            {
                h.ServiceManager.EnabledServices.Remove("shelf");

                Assert.Equal(0, await UpdateAsync(h));

                Assert.Equal(restarts, h.HostRestarts());
                Assert.Equal(new[] { "images -> 2.0.0" }, h.LinkedExtensions());
                Assert.Equal(!newHost, h.Output.Messages.Contains("shelf: restart skipped, as its service is disabled."));
            }
        }

        [Theory]
        [InlineData(false, "the extensions it started with")]
        [InlineData(true, "1.0.0")]
        public async Task Update_ManualRestart_RestartsAndChecksNothing(bool newHost, string runs)
        {
            using (AppHarness h = await InstalledAsync(newHost, newImages: true))
            {
                await h.SetRestart().ExecuteAsync(new[] { "shelf", "manual" });

                Assert.Equal(0, await UpdateAsync(h));

                Assert.Equal(0, h.HostRestarts());
                Assert.Equal(new[] { "images -> 2.0.0" }, h.LinkedExtensions());
                Assert.Contains($"shelf: restart skipped (restart=manual); the service runs {runs} until it restarts.", h.Output.Messages);
            }
        }

        [Fact]
        public async Task Update_OneExtension_LeavesItsHostAndTheOthers()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            {
                h.AddExtensionRelease("me/video", "1.0.0");
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/video", "--from", "github:me/video" }));
                h.AddExtensionRelease("me/video", "2.0.0");
                h.ForgetCalls();
                await h.RunLog.SaveAsync(new UpdateRun(h.Time.GetUtcNow(), UpdateStarter.Timer, new[] { new UpdateFailure("shelf", "no network"), new UpdateFailure("shelf/images", "refused") }));

                Assert.Equal(0, await UpdateAsync(h, "shelf/images"));

                Assert.Equal(new[] { "images -> 2.0.0", "video -> 1.0.0" }, h.LinkedExtensions());
                Assert.Equal(One, await h.Versions.GetCurrentAsync(Shelf));
                Assert.Equal(new[] { ("1.0.0", "2.0.0") }, Asked(h));
                Assert.Equal(new[] { "shelf" }, (await h.RunLog.LoadAsync())!.Failures.Select(failure => failure.Subject));
            }
        }

        [Fact]
        public async Task Update_OneAppWhoseExtensionIsRefused_ClearsOnlyTheAppsFailure()
        {
            using (AppHarness h = await InstalledAsync())
            {
                await h.RunLog.SaveAsync(new UpdateRun(h.Time.GetUtcNow(), UpdateStarter.Timer, new[] { new UpdateFailure("shelf", "no network"), new UpdateFailure("shelf/images", "refused") }));
                Refuse(h, ("1.0.0", "1.0.0"));

                Assert.Equal(1, await UpdateAsync(h, "shelf"));

                Assert.Equal(new[] { "shelf/images" }, (await h.RunLog.LoadAsync())!.Failures.Select(failure => failure.Subject));
            }
        }

        [Fact]
        public async Task Update_ExtensionNotInstalled_Fails()
        {
            using (AppHarness h = await InstalledAsync())
            {
                Assert.Equal(1, await UpdateAsync(h, "shelf/video"));

                Assert.Equal(new[] { "Error: 'shelf/video' is not installed." }, h.Output.Errors);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("shelf/images")]
        public async Task Update_ExtensionWithoutACurrentVersion_IsReportedOnce(string? named)
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            {
                h.Versions.Unlink(Images);

                Assert.Equal(1, await UpdateAsync(h, named));

                Assert.Equal(new[] { "Error: shelf/images: shelf/images has no current version. Remove it with 'updaemon remove shelf/images', then install it again." }, h.Output.Errors);
                Assert.Equal(named == null ? Two : One, await h.Versions.GetCurrentAsync(Shelf));
                Assert.Empty(h.LinkedExtensions());
            }
        }

        [Fact]
        public async Task Update_OneExtensionOfAKilledInstall_SaysItIsNotInstalled()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                await h.KilledInstallOfImagesAsync();

                Assert.Equal(1, await UpdateAsync(h, "shelf/images"));

                Assert.Equal(new[] { "Error: 'shelf/images' is not installed." }, h.Output.Errors);
            }
        }

        [Fact]
        public async Task Update_Killed_TheNextRunPutsBothBackFirstAndUpdatesAgain()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true, hostManifest: ChangedManifest))
            {
                h.ServiceManager.OnRestart = h.KeepForCutOff;
                Assert.Equal(0, await UpdateAsync(h, "shelf"));
                h.CutOff();

                Assert.Equal(0, await UpdateAsync(h, "shelf"));

                Assert.Contains("shelf: an earlier run did not finish switching versions; putting back shelf 1.0.0, shelf/images 1.0.0.", h.Output.Messages);
                await AssertCurrentAsync(h, Two, "2.0.0");
                Assert.Empty((await h.HostAsync()).SkippedVersions);
            }
        }

        [Fact]
        public async Task Update_KilledBeforeTheSwitch_TheNextRunDownloadsAgain()
        {
            using (AppHarness h = await InstalledAsync(newHost: true, newImages: true))
            {
                h.AnswerCheck = _ =>
                {
                    h.KeepForCutOff();
                    return Accepted;
                };
                Assert.Equal(0, await UpdateAsync(h, "shelf"));
                h.CutOff();
                await AssertCurrentAsync(h, One, "1.0.0");

                Assert.Equal(0, await UpdateAsync(h, "shelf"));

                await AssertCurrentAsync(h, Two, "2.0.0");
            }
        }

        [Fact]
        public async Task Update_PrunesAnExtensionsOldVersionsAndForgetsTheirSkips()
        {
            using (AppHarness h = await InstalledAsync())
            {
                await h.Config.SaveConfigAsync(await WithRetentionAsync(h, 2));
                h.AddExtensionRelease(ImagesRemote, "1.1.0");
                h.ServiceManager.CrashOnNextStart.Add("shelf");
                Assert.Equal(1, await UpdateAsync(h));
                Assert.Equal(new[] { new Version(1, 1, 0) }, (await h.HostAsync()).FindExtension("images")!.SkippedVersions);
                foreach (string version in new[] { "2.0.0", "3.0.0" })
                {
                    h.AddExtensionRelease(ImagesRemote, version);
                    Assert.Equal(0, await UpdateAsync(h));
                }

                Assert.Equal(new[] { Two, new Version(3, 0, 0) }, h.Versions.GetRetained(Images));
                Assert.Empty((await h.HostAsync()).FindExtension("images")!.SkippedVersions);
                Assert.Equal(new[] { One }, h.Versions.GetRetained(Shelf));
            }
        }

        /// <summary>Its skipped 1.1.0 is older than the 2.0.0 it is updated to, and not kept.</summary>
        private static async Task<AppHarness> WithASkipToForgetAsync()
        {
            AppHarness h = await InstalledAsync(newImages: true);
            InstalledApp host = await h.HostAsync();
            host.FindExtension("images")!.RecordSwitch(from: new Version(1, 1, 0), to: One);
            await h.Config.SaveAppAsync(host);
            return h;
        }

        [Fact]
        public async Task Update_ForgottenSkipsCannotBeSaved_OnlyWarns()
        {
            using (AppHarness h = await WithASkipToForgetAsync())
            {
                h.SavedConfig.FailNext(app => app.Unfinished == null && app.FindExtension("images")!.SkippedVersions.Count == 0);

                Assert.Equal(0, await UpdateAsync(h));

                Assert.Equal(new[] { "Warning: shelf: failed to clean up after the update: disk full" }, h.Output.Errors);
                await AssertCurrentAsync(h, One, "2.0.0");
                Assert.Empty((await h.RunLog.LoadAsync())!.Failures);
            }
        }

        [SkippableFact]
        public async Task Update_OldVersionsCannotBeListed_OnlyWarns()
        {
            Unwritable.SkipUnderRoot();
            using (AppHarness h = await WithASkipToForgetAsync())
            {
                string versions = h.Scope.Locate(Images).VersionsDirectory;
                UnixFileMode mode = File.GetUnixFileMode(versions);
                h.ServiceManager.OnRestart = () => File.SetUnixFileMode(versions, UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                try
                {
                    Assert.Equal(0, await UpdateAsync(h));
                }
                finally
                {
                    File.SetUnixFileMode(versions, mode);
                }

                Assert.StartsWith("Warning: shelf: failed to clean up after the update: ", Assert.Single(h.Output.Errors));
                await AssertCurrentAsync(h, One, "2.0.0");
            }
        }

        private static async Task<UpdaemonConfig> WithRetentionAsync(AppHarness h, int count)
        {
            UpdaemonConfig config = await h.Config.LoadConfigAsync();
            config.ReleaseRetentionCount = count;
            return config;
        }

        private static IEnumerable<string?> Started(AppHarness h) =>
            h.Client.MethodCalls.Where(call => call.StartsWith(nameof(IDistributionServiceClient.ConnectAsync))).Select(Path.GetFileName);

        [Fact]
        public async Task Update_AppsFromTwoPlugins_StartsEachOnce()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.AddPluginAsync("github");
                await h.AddPluginAsync("other");
                foreach ((string name, string plugin) in new[] { ("a", "github"), ("b", "other"), ("c", "github") })
                {
                    await InstallToolAsync(h, name, plugin);
                }

                h.ForgetCalls();

                Assert.Equal(0, await UpdateAsync(h));

                Assert.Equal(new[] { "github", "other" }, Started(h));
            }
        }

        [Fact]
        public async Task Update_ExtensionsFromTwoPlugins_StartsEachOnce()
        {
            using (AppHarness h = await InstalledAsync())
            {
                await h.AddPluginAsync("other");
                h.AddExtensionRelease("audio", "1.0.0");
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/audio" }));
                h.AddExtensionRelease("me/video", "1.0.0");
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/video", "--from", "github:me/video" }));
                h.ForgetCalls();

                Assert.Equal(0, await UpdateAsync(h));

                Assert.Equal(new[] { "github", "other" }, Started(h));
            }
        }

        /// <summary>The apps after it do not wait for one host's trouble.</summary>
        [Fact]
        public async Task Update_OneHostFails_TheOtherAppsAreUpdated()
        {
            using (AppHarness h = await InstalledAsync(newHost: true))
            {
                await InstallToolAsync(h, "tool", "github");
                h.ServiceManager.CrashOnNextStart.Add("shelf");

                Assert.Equal(1, await UpdateAsync(h));

                Assert.StartsWith("Error: shelf: 2.0.0 did not stay running, so 1.0.0 runs again", Assert.Single(h.Output.Errors));
                await AssertCurrentAsync(h, One, "1.0.0");
                Assert.Equal(Two, await h.Versions.GetCurrentAsync(new AppName("tool")));
            }
        }

        [Fact]
        public async Task Update_ExtensionsCurrentVersionIsGone_ReportsItAndUpdatesTheHost()
        {
            using (AppHarness h = await InstalledAsync(newHost: true))
            {
                Directory.Delete(h.Versions.GetVersionDirectory(Images, One), recursive: true);

                Assert.Equal(1, await UpdateAsync(h));

                Assert.Equal(new[] { "Error: shelf/images: shelf/images 1.0.0 is not kept. Kept versions: none." }, h.Output.Errors);
                Assert.Equal(Two, await h.Versions.GetCurrentAsync(Shelf));
            }
        }
    }
}
