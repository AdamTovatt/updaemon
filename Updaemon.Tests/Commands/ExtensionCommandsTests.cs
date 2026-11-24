using Updaemon.Commands;
using Updaemon.Configuration;
using Updaemon.Interfaces;
using Updaemon.IO;
using Updaemon.Models;
using Updaemon.Services;
using Updaemon.Tests.Helpers;
using Updaemon.Tests.Mocks;
using static Updaemon.Tests.Helpers.ShelfPackages;

namespace Updaemon.Tests.Commands
{
    /// <summary>install, remove and status of an extension, and the commands that take only its host.</summary>
    public class ExtensionCommandsTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Install_KnownExtension_ChecksLinksAndRestartsTheHost(bool isSystem, bool isMacOS)
        {
            using (AppHarness h = new AppHarness(isSystem, isMacOS))
            {
                await h.InstallHostAsync();
                h.ServiceManager.MethodCalls.Clear();

                int exitCode = await h.Install().ExecuteAsync(new[] { "shelf/images" });

                Assert.Empty(h.Output.Errors);
                Assert.Equal(0, exitCode);
                string version = Path.Combine(h.Scope.GetAppDirectory("shelf"), "extension-versions", "images", "1.0.0");
                Assert.Equal(version, new FileInfo(Path.Combine(h.Scope.GetAppDirectory("shelf"), "extensions", "images")).LinkTarget);
                Assert.Equal(new[] { "images -> 1.0.0" }, h.LinkedExtensions());
                Assert.Equal(1, h.HostRestarts());
                Assert.Contains("Installed shelf/images 1.0.0 into shelf 1.0.0.", h.Output.Messages);
                InstalledApp host = await h.HostAsync();
                InstalledExtension extension = Assert.Single(host.Extensions);
                Assert.Equal(("images", "github:owner/shelf/images.zip"), (extension.Id, extension.Source.ToString()));
                Assert.Null(host.Unfinished);
            }
        }

        /// <summary>A root's check must not run as root for a host that does not.</summary>
        [Theory]
        [InlineData(true, "daemon")]
        [InlineData(false, null)]
        public async Task Install_RunsTheCurrentHostsCheckOnTheDownload_AsTheUserTheHostRunsAs(bool isSystem, string? expectedUser)
        {
            using (AppHarness h = new AppHarness(isSystem))
            {
                await h.InstallHostAsync();
                string directory = h.Versions.GetVersionDirectory(new ExtensionName("shelf", "images"), new Version(1, 0, 0));
                h.AnswerCheck = _ =>
                {
                    Assert.Contains(directory + ".partial", h.Permissions.TreesMadeReadable);
                    Assert.True(File.Exists(Path.Combine(directory, "plugin.dll")));
                    Assert.Empty(h.LinkedExtensions());
                    return new ProcessRunner.Result(0, string.Empty, string.Empty);
                };

                await h.Install().ExecuteAsync(new[] { "shelf/images" });

                ProcessInvocation check = Assert.Single(h.Checks);
                Assert.Equal(expectedUser, check.User);
                Assert.Equal(h.Versions.GetVersionDirectory(new AppName("shelf"), new Version(1, 0, 0)), check.WorkingDirectory);
                Assert.Equal(new[] { "--check-plugin", directory }, check.Arguments);
            }
        }

        [Theory]
        [InlineData("service")]
        [InlineData("command")]
        public async Task Variable_IsSetForTheServiceAndTheCommand_WithOrWithoutExtensions(string kind)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                string wrapper = kind == "service" ? h.Scope.GetServiceWrapperPath("shelf") : h.Scope.GetCommandPath("shelf");
                string expected = $"SHELF_PLUGINS={h.Scope.GetExtensionsDirectory("shelf")}\n";

                Assert.Equal(expected, await Shell.RunAsync(wrapper));
                Assert.Empty(h.LinkedExtensions());

                await h.Install().ExecuteAsync(new[] { "shelf/images" });
                Assert.Equal(expected, await Shell.RunAsync(wrapper));
            }
        }

        /// <summary>An updaemon that knew no extensions ignored the field when it activated the host.</summary>
        [Fact]
        public async Task Install_HostActivatedWithoutItsVariable_SetsItBeforeTheRestart()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                string wrapper = h.Scope.GetServiceWrapperPath("shelf");
                File.WriteAllText(wrapper, "from before");
                Directory.Delete(h.Scope.GetExtensionsDirectory("shelf"));
                string atRestart = string.Empty;
                h.ServiceManager.OnRestart = () => atRestart = File.ReadAllText(wrapper);

                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Contains("SHELF_PLUGINS=", atRestart);
            }
        }

        [Fact]
        public async Task Install_FromGiven_ComesFromThereAndIsStored()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.AddExtensionRelease("me/fork", "3.0.0");

                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/images", "--from", "github:me/fork" }));

                Assert.Equal(new[] { "images -> 3.0.0" }, h.LinkedExtensions());
                Assert.Equal("github:me/fork", Assert.Single((await h.HostAsync()).Extensions).Source.ToString());
            }
        }

        [Fact]
        public async Task Install_HostRefusesIt_InstallsNothingAndSaysWhy()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.AnswerCheck = _ => new ProcessRunner.Result(1, string.Empty, "needs shelf 2\n");
                h.ServiceManager.MethodCalls.Clear();
                IReadOnlyList<string> before = h.Snapshot();

                int exitCode = await h.Install().ExecuteAsync(new[] { "shelf/images" });

                Assert.Equal(1, exitCode);
                Assert.Equal(new[] { "Error: shelf/images was not installed: shelf 1.0.0 refuses 1.0.0: needs shelf 2." }, h.Output.Errors);
                Assert.Empty(h.ServiceManager.MethodCalls);
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Install_HostDoesNotStayUpWithIt_RemovesItAgainAndRestartsTheHost()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.ServiceManager.CrashOnNextStart.Add("shelf");
                h.ServiceManager.MethodCalls.Clear();
                IReadOnlyList<string> before = h.Snapshot();

                int exitCode = await h.Install().ExecuteAsync(new[] { "shelf/images" });

                Assert.Equal(1, exitCode);
                Assert.Equal(new[] { "Error: installing shelf/images failed: shelf did not stay running with 1.0.0, so it is removed again. See 'updaemon logs shelf'." }, h.Output.Errors);
                Assert.Equal(2, h.HostRestarts());
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Install_RestartFails_RemovesItAgain()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.ServiceManager.RestartFailure = new InvalidOperationException("no restart today");

                int exitCode = await h.Install().ExecuteAsync(new[] { "shelf/images" });

                Assert.Equal(1, exitCode);
                Assert.Equal(new[] { "Error: installing shelf/images failed: no restart today, and switching back failed: no restart today" }, h.Output.Errors);
                Assert.Empty(h.LinkedExtensions());
                // The restart on what is put back is still owed, so the next change makes up for it.
                Assert.IsType<UnfinishedChange.PutBack>((await h.HostAsync()).Unfinished);
                h.ServiceManager.RestartFailure = null;
                h.ServiceManager.MethodCalls.Clear();
                await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" });
                Assert.Equal(1, h.HostRestarts());
                Assert.Empty((await h.HostAsync()).Extensions);
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        [Fact]
        public async Task Install_CancelledDuringTheHealthCheck_RemovesItAgainAndRestartsTheHost()
        {
            using (AppHarness h = new AppHarness())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                await h.InstallHostAsync();
                h.ServiceManager.MethodCalls.Clear();
                IReadOnlyList<string> before = h.Snapshot();
                h.ServiceManager.OnRestart = cancellation.Cancel;

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Install().ExecuteAsync(new[] { "shelf/images" }, cancellation.Token));

                Assert.Equal(2, h.HostRestarts());
                h.AssertUnchangedSince(before);
            }
        }

        [Theory]
        [MemberData(nameof(HostsLeftRunning), MemberType = typeof(ShelfPackages))]
        public async Task Install_HostManualOrDisabled_LeavesItRunning(string message, Func<AppHarness, Task> leaveRunning)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                await leaveRunning(h);
                h.ServiceManager.MethodCalls.Clear();

                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(new[] { "images -> 1.0.0" }, h.LinkedExtensions());
                Assert.Contains(message, h.Output.Messages);
                Assert.Equal(0, h.HostRestarts());
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        [Theory]
        [MemberData(nameof(HostsLeftRunning), MemberType = typeof(ShelfPackages))]
        public async Task Remove_HostManualOrDisabled_LeavesItRunning(string message, Func<AppHarness, Task> leaveRunning)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                await leaveRunning(h);

                Assert.Equal(0, await h.Remove().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Empty(h.LinkedExtensions());
                Assert.Contains(message, h.Output.Messages);
                Assert.Equal(0, h.HostRestarts());
                Assert.Null((await h.HostAsync()).Unfinished);
                // Its service may still read them. They go when it is installed again.
                Assert.Equal(new[] { One }, h.Versions.GetRetained(Images));
                h.AddExtensionRelease(ImagesRemote, "2.0.0");
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/images" }));
                Assert.Equal(new[] { Two }, h.Versions.GetRetained(Images));
            }
        }

        [Fact]
        public async Task Install_CommandOnlyHost_NeedsNoServiceManager()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync(CommandOnlyManifest);
                h.ServiceManager.Unreachable = "unreachable";
                h.ServiceManager.MethodCalls.Clear();

                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal($"SHELF_PLUGINS={h.Scope.GetExtensionsDirectory("shelf")}\n", await Shell.RunAsync(h.Scope.GetCommandPath("shelf")));
                Assert.Empty(h.ServiceManager.MethodCalls);
            }
        }

        [Fact]
        public async Task Remove_CommandOnlyHost_NeedsNoServiceManager()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync(CommandOnlyManifest);
                h.ServiceManager.Unreachable = "unreachable";

                Assert.Equal(0, await h.Remove().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Empty(h.LinkedExtensions());
                Assert.Empty(h.Versions.GetRetained(Images));
                Assert.Empty(h.ServiceManager.MethodCalls);
            }
        }

        [Fact]
        public async Task Install_ServiceManagerUnreachable_WritesNothing()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.ServiceManager.Unreachable = "unreachable";
                IReadOnlyList<string> before = h.Snapshot();

                await Assert.ThrowsAsync<ServiceManagerUnreachableException>(() => h.Install().ExecuteAsync(new[] { "shelf/images" }));

                h.AssertUnchangedSince(before);
            }
        }

        [Theory]
        [InlineData("Error: 'nohost' is not installed, and nohost/images is an extension of it. Install it first: updaemon install nohost --from <plugin>:<remote>", "nohost/images")]
        [InlineData("Error: shelf 1.0.0 does not know an extension 'video'; it knows audio, images. For another, say where it comes from: updaemon install shelf/video --from <plugin>:<remote>", "shelf/video")]
        [InlineData("Error: plain 1.0.0 takes no extensions: its updaemon.json has no 'extensions'. A newer release may; get it with 'updaemon update plain'.", "plain/images")]
        [InlineData("Error: 'shelf/a/b' is not a valid extension name. Use <app>/<extension>, each of letters, digits, '.', '_' and '-'; 'updaemon' is reserved.", "shelf/a/b")]
        [InlineData("Error: --from must be <plugin>:<remote>, for example github:owner/repo. Got 'x'.", "shelf/images", "--from", "x")]
        [InlineData("Error: --set does not go with an extension, which has no settings of its own. Use 'updaemon set shelf KEY=VALUE' for what it reads.", "shelf/images", "--set", "A=1")]
        [InlineData("Error: --command does not go with an extension: its host, shelf, loads it.", "shelf/images", "--command", "x")]
        [InlineData("Error: --service does not go with an extension: its host, shelf, loads it.", "shelf/images", "--service")]
        [InlineData("Error: --run-as does not go with an extension: its host, shelf, loads it.", "shelf/images", "--run-as", "ada")]
        public async Task Install_CannotBe_SaysWhatToDoAndWritesNothing(string error, params string[] args)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.AddRelease("owner/plain", "1.0.0", """{ "executable": "bin/plain", "command": "plain" }""", "bin/plain");
                await h.Install().ExecuteAsync(new[] { "plain", "--from", "github:owner/plain" });
                IReadOnlyList<string> before = h.Snapshot();

                Assert.Equal(1, await h.Install().ExecuteAsync(args));

                Assert.Equal(new[] { error }, h.Output.Errors);
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Install_HostKnowsNoExtensions_SaysSo()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync("""{ "executable": "bin/shelf", "command": "shelf", "extensions": { "variable": "SHELF_PLUGINS" } }""");

                Assert.Equal(1, await h.Install().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(
                    new[] { "Error: shelf 1.0.0 does not know an extension 'images'; it knows none. For another, say where it comes from: updaemon install shelf/images --from <plugin>:<remote>" },
                    h.Output.Errors);
            }
        }

        public static TheoryData<string, Func<AppHarness, Task<int>>, string> CommandsOnAnotherCase => new TheoryData<string, Func<AppHarness, Task<int>>, string>
        {
            { "update", h => h.Update().ExecuteAsync(new[] { "shelf/IMAGES" }), "Updated shelf/images from 1.0.0 to 2.0.0." },
            {
                "rollback",
                async h =>
                {
                    Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf/images" }));
                    return await h.RollbackCommand().ExecuteAsync(new[] { "shelf/IMAGES" });
                },
                "Rolled back shelf/images from 2.0.0 to 1.0.0. 'updaemon update' skips 2.0.0 from now on."
            },
            { "remove", h => h.Remove().ExecuteAsync(new[] { "shelf/IMAGES" }), "Removed shelf/images." },
        };

        /// <summary>The installed one is meant, and is called by its own name.</summary>
        [Theory]
        [MemberData(nameof(CommandsOnAnotherCase))]
        public async Task Command_IdDiffersOnlyInCase_IsTheInstalledOne(string name, Func<AppHarness, Task<int>> run, string message)
        {
            _ = name; // Names the case in the test's display name.
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                h.AddExtensionRelease(ImagesRemote, "2.0.0");

                Assert.Equal(0, await run(h));

                Assert.Contains(message, h.Output.Messages);
                Assert.Empty(h.Output.Errors);
            }
        }

        [Fact]
        public async Task Status_IdDiffersOnlyInCase_IsTheInstalledOne()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                Assert.Equal(0, await h.Status().ExecuteAsync(new[] { "shelf/images" }));
                List<string> asInstalled = h.Output.Messages.ToList();
                h.Output.Clear();

                Assert.Equal(0, await h.Status().ExecuteAsync(new[] { "shelf/IMAGES" }));

                Assert.Equal(asInstalled, h.Output.Messages);
            }
        }

        [Theory]
        [InlineData(false, "Error: 'shelf' is not installed for user ada, but for the whole machine. An extension goes where its host is: run this with sudo.")]
        [InlineData(true, "Error: 'shelf' is not installed for the whole machine, but for user ada. An extension goes where its host is: run this without sudo.")]
        public async Task Install_HostInTheOtherScope_NamesThatScope(bool isSystem, string error)
        {
            using (AppHarness h = new AppHarness(isSystem))
            {
                await new ConfigManager(h.OtherScope).SaveAppAsync(new InstalledApp { Name = "shelf", Plugin = "github", Remote = "owner/shelf" });

                Assert.Equal(1, await h.Install().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(new[] { error }, h.Output.Errors);
            }
        }

        [Fact]
        public async Task Install_VersionsLeftOfAnEarlierInstall_AreGone()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                Directory.CreateDirectory(h.Versions.GetVersionDirectory(new ExtensionName("shelf", "images"), new Version(0, 9, 0)));

                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(new[] { new Version(1, 0, 0) }, h.Versions.GetRetained(new ExtensionName("shelf", "images")));
            }
        }

        [Fact]
        public async Task Install_AlreadyInstalled_PointsToUpdate()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();

                Assert.Equal(1, await h.Install().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(new[] { "Error: 'shelf/images' is already installed. Use 'updaemon update shelf/images' to update it." }, h.Output.Errors);
            }
        }

        /// <summary>On macOS their directories are one, so a second would replace the first.</summary>
        [Fact]
        public async Task Install_IdDiffersOnlyInCase_IsTheInstalledOne()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                IReadOnlyList<string> before = h.Snapshot();

                Assert.Equal(1, await h.Install().ExecuteAsync(new[] { "shelf/Images", "--from", "github:owner/shelf/images.zip" }));

                Assert.Equal(new[] { "Error: 'shelf/images' is already installed. Use 'updaemon update shelf/images' to update it." }, h.Output.Errors);
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Install_NoRelease_FailsNamingTheSource()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.Client.LatestVersions["owner/shelf/images.zip"] = null;

                Assert.Equal(1, await h.Install().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(new[] { "Error: shelf/images: github:owner/shelf/images.zip has no released version." }, h.Output.Errors);
            }
        }

        [Fact]
        public async Task Install_PluginFails_FailsWithWhatItSaid()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.Client.RefusedRemotes["owner/gone"] = "no such repository";

                Assert.Equal(1, await h.Install().ExecuteAsync(new[] { "shelf/gone", "--from", "github:owner/gone" }));

                Assert.Equal(new[] { "Error: shelf/gone: no such repository" }, h.Output.Errors);
            }
        }

        [Fact]
        public async Task Install_PluginNeedsASecretWithoutATerminal_NamesTheCommand()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.AddExtensionRelease("owner/private", "1.0.0");
                h.Client.TokenProtectedRemotes["owner/private"] = "tok";
                h.Prompter.IsInteractive = false;
                IReadOnlyList<string> before = h.Snapshot();

                Assert.Equal(1, await h.Install().ExecuteAsync(new[] { "shelf/private", "--from", "github:owner/private" }));

                Assert.StartsWith("Error: shelf/private: ", Assert.Single(h.Output.Errors));
                Assert.EndsWith("Set it with 'updaemon secret-set github githubToken'.", h.Output.Errors[0]);
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Install_ArchiveWithAManifest_IgnoresIt()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.AfterDownloading("owner/shelf/images.zip", "1.0.0", directory => File.WriteAllText(Path.Combine(directory, AppManifest.FileName), "not json"));

                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/images" }));
            }
        }

        [Fact]
        public async Task Remove_Extension_UnlinksDeletesAndRestartsTheHost()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                await h.RunLog.SaveAsync(new UpdateRun(h.Time.GetUtcNow(), UpdateStarter.Timer, new[] { new UpdateFailure("shelf/images", "refused"), new UpdateFailure("shelf", "other") }));
                IReadOnlyList<Version> atRestart = Array.Empty<Version>();
                h.ServiceManager.OnRestart = () => atRestart = h.Versions.GetRetained(Images);

                int exitCode = await h.Remove().ExecuteAsync(new[] { "shelf/images" });

                Assert.Equal(0, exitCode);
                Assert.Equal(new[] { "Removed shelf/images." }, h.Output.Messages);
                Assert.Empty(h.LinkedExtensions());
                // Until the host is restarted, it may read them.
                Assert.Equal(new[] { One }, atRestart);
                Assert.False(Directory.Exists(Path.Combine(h.Scope.GetAppDirectory("shelf"), "extension-versions")));
                InstalledApp host = await h.HostAsync();
                Assert.Empty(host.Extensions);
                Assert.Null(host.Unfinished);
                Assert.Equal(1, h.HostRestarts());
                Assert.Equal(new[] { "shelf" }, (await h.RunLog.LoadAsync())!.Failures.Select(failure => failure.Subject));
            }
        }

        [Fact]
        public async Task Remove_Extension_HostDoesNotStayUp_TheRemovalStaysAndItFails()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                h.ServiceManager.CrashOnNextStart.Add("shelf");

                int exitCode = await h.Remove().ExecuteAsync(new[] { "shelf/images" });

                Assert.Equal(1, exitCode);
                Assert.Equal(new[] { "Error: shelf did not stay running after the restart. shelf/images is removed. See 'updaemon logs shelf'." }, h.Output.Errors);
                Assert.Empty((await h.HostAsync()).Extensions);
                Assert.Empty(h.LinkedExtensions());
                Assert.Equal(1, h.HostRestarts());
            }
        }

        [Fact]
        public async Task Remove_OneOfTwoExtensions_LeavesTheOther()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                h.AddExtensionRelease("owner/audio", "3.0.0");
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/audio", "--from", "github:owner/audio" }));

                Assert.Equal(0, await h.Remove().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(new[] { "audio -> 3.0.0" }, h.LinkedExtensions());
                Assert.Equal("audio", Assert.Single((await h.HostAsync()).Extensions).Id);
            }
        }

        [Fact]
        public async Task Remove_ExtensionWithPurge_IsRefused()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                IReadOnlyList<string> before = h.Snapshot();

                Assert.Equal(1, await h.Remove().ExecuteAsync(new[] { "shelf/images", "--purge" }));

                Assert.Equal(new[] { "Error: --purge does not go with an extension, which has no data of its own. Use 'updaemon remove shelf/images'." }, h.Output.Errors);
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Remove_Extension_CannotBeRecorded_PutsItBack()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                IReadOnlyList<string> before = h.Snapshot();
                h.SavedConfig.FailNext(app => app.Unfinished is not UnfinishedChange.PutBack);

                IOException ex = await Assert.ThrowsAsync<IOException>(() => h.Remove().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal("disk full", ex.Message);
                h.AssertUnchangedSince(before);
                Assert.Equal(0, h.HostRestarts());
            }
        }

        [Fact]
        public async Task Remove_Extension_WhatToPutBackCannotBeSaved_ChangesNothing()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                IReadOnlyList<string> before = h.Snapshot();
                h.SavedConfig.FailNext(app => app.Unfinished is UnfinishedChange.PutBack);

                await Assert.ThrowsAsync<IOException>(() => h.Remove().ExecuteAsync(new[] { "shelf/images" }));

                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Remove_Extension_RestartFails_TheRemovalStaysAndItSaysBoth()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                await h.RunLog.SaveAsync(new UpdateRun(h.Time.GetUtcNow(), UpdateStarter.Timer, new[] { new UpdateFailure("shelf/images", "refused") }));
                h.ServiceManager.RestartFailure = new InvalidOperationException("no restart today.");

                Assert.Equal(1, await h.Remove().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(new[] { "Removed shelf/images." }, h.Output.Messages);
                Assert.Equal(new[] { "Error: shelf could not be restarted: no restart today. shelf/images is removed. See 'updaemon logs shelf'." }, h.Output.Errors);
                Assert.Empty((await h.RunLog.LoadAsync())!.Failures);
                // Still owed to the next change, so the host may read what it runs with.
                Assert.True((await h.HostAsync()).Unfinished!.Restart);
                Assert.Equal(new[] { One }, h.Versions.GetRetained(Images));
            }
        }

        [Fact]
        public async Task Remove_Extension_CancelledDuringTheHealthCheck_LeavesNothingToMakeUpFor()
        {
            using (AppHarness h = new AppHarness())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                await h.InstallHostWithImagesAsync();
                h.ServiceManager.OnRestart = cancellation.Cancel;

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Remove().ExecuteAsync(new[] { "shelf/images" }, cancellation.Token));

                Assert.Empty((await h.HostAsync()).Extensions);
                Assert.Null((await h.HostAsync()).Unfinished);
            }
        }

        /// <summary>Its versions are left for the next install to clear; it is out of the host all the same.</summary>
        [SkippableFact]
        public async Task Remove_Extension_VersionsCannotBeDeleted_WarnsAndGoesOn()
        {
            Unwritable.SkipUnderRoot();
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                string versions = h.Scope.Locate(new ExtensionName("shelf", "images")).VersionsDirectory;

                int exitCode = await Unwritable.WhileAsync(versions, () => h.Remove().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(0, exitCode);
                Assert.StartsWith("Warning: could not delete the versions of shelf/images: ", Assert.Single(h.Output.Errors));
                Assert.Empty(h.LinkedExtensions());
                Assert.Equal(1, h.HostRestarts());
            }
        }

        /// <summary>A killed install is taken out by the removal itself, before it looks for what to remove.</summary>
        [Fact]
        public async Task Remove_ExtensionOfAKilledInstall_TakesItOutAndSaysItIsNotInstalled()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                IReadOnlyList<string> before = h.Snapshot();
                await h.KilledInstallOfImagesAsync();

                Assert.Equal(1, await h.Remove().ExecuteAsync(new[] { "shelf/images" }));

                Assert.Equal(new[] { "Error: 'shelf/images' is not installed." }, h.Output.Errors);
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Remove_FailuresCannotBeForgotten_WarnsAndGoesOn()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                await File.WriteAllTextAsync(h.Scope.LastUpdateFilePath, "not json");

                Assert.Equal(0, await h.Remove().ExecuteAsync(new[] { "shelf/images" }));

                Assert.StartsWith("Warning: the last run's failures still name it in 'updaemon status': ", Assert.Single(h.Output.Errors));
                Assert.Equal(new[] { "Removed shelf/images." }, h.Output.Messages);
            }
        }

        [Fact]
        public async Task Remove_Host_RemovesItsExtensionsAndNamesThem()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                h.AddExtensionRelease("me/video", "1.0.0");
                await h.Install().ExecuteAsync(new[] { "shelf/video", "--from", "github:me/video" });
                UpdateFailure other = new UpdateFailure("other", "not this one");
                await h.RunLog.SaveAsync(new UpdateRun(h.Time.GetUtcNow(), UpdateStarter.Timer, new[] { new UpdateFailure("shelf", "crashed"), new UpdateFailure("shelf/video", "refused"), other }));
                h.Output.Clear();

                Assert.Equal(0, await h.Remove().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal(new[] { other }, (await h.RunLog.LoadAsync())!.Failures);

                Assert.Equal(new[] { $"Removed shelf, its extensions shelf/images, shelf/video. shelf's data is kept in {h.Scope.GetDataDirectory("shelf")}." }, h.Output.Messages);
                Assert.False(Directory.Exists(h.Scope.GetAppDirectory("shelf")));
            }
        }

        [Theory]
        [InlineData(null, "shelf         1.0.0  command + service  running  github:owner/shelf", "shelf/audio   1.0.0  extension          -        github:me/audio", "shelf/images  1.0.0  extension          -        github:owner/shelf/images.zip  skips 2.0.0")]
        [InlineData("shelf/images", "shelf/images  1.0.0  extension  -  github:owner/shelf/images.zip  skips 2.0.0")]
        public async Task Status_ListsExtensionsUnderTheirHost_WithoutRunningTheCheck(string? named, params string[] expectedRows)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                h.AddExtensionRelease("me/audio", "1.0.0");
                await h.Install().ExecuteAsync(new[] { "shelf/audio", "--from", "github:me/audio" });
                InstalledApp host = await h.HostAsync();
                host.FindExtension("images")!.RecordSwitch(new Version(2, 0, 0), new Version(1, 0, 0));
                await h.Config.SaveAppAsync(host);
                h.Output.Clear();
                h.Checks.Clear();

                Assert.Equal(0, await h.Status().ExecuteAsync(named == null ? Array.Empty<string>() : new[] { named }));

                string[] rows = h.Output.Messages.SkipWhile(line => line.Length > 0).Skip(1).ToArray();
                Assert.Equal(expectedRows, rows);
                Assert.Empty(h.Checks);
            }
        }

        public static TheoryData<string, Func<AppHarness, ICommand>, string[], string> HostOnlyCommands => new TheoryData<string, Func<AppHarness, ICommand>, string[], string>
        {
            { "logs", h => h.Logs(), new[] { "shelf/images" }, "updaemon logs shelf [-f]" },
            { "set", h => h.Set(), new[] { "shelf/images", "A=secret" }, "updaemon set shelf KEY=VALUE... [--secret]" },
            { "unset", h => h.Unset(), new[] { "shelf/images", "A" }, "updaemon unset shelf KEY..." },
            { "set-args", h => h.SetArgs(), new[] { "shelf/images", "--", "x" }, "updaemon set-args shelf [-- args...]" },
            { "set-restart", h => h.SetRestart(), new[] { "shelf/images", "manual" }, "updaemon set-restart shelf auto|manual" },
        };

        [Theory]
        [MemberData(nameof(HostOnlyCommands))]
        public async Task HostOnlyCommand_OnAnExtension_FailsNamingTheHostsCommand(string name, Func<AppHarness, ICommand> command, string[] args, string hostCommand)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();

                Assert.Equal(1, await command(h).ExecuteAsync(args, CancellationToken.None));

                Assert.Equal(new[] { $"Error: 'shelf/images' names an extension, which '{name}' does not work on. For its host, use '{hostCommand}'." }, h.Output.Errors);
            }
        }

        public static TheoryData<string, Func<AppHarness, ICommand>, string[]> ChangesToTheHostsVariable => new TheoryData<string, Func<AppHarness, ICommand>, string[]>
        {
            { "set", h => h.Set(), new[] { "shelf", "SHELF_PLUGINS=/elsewhere" } },
            { "set --secret", h => h.Set(), new[] { "shelf", "SHELF_PLUGINS=/elsewhere", "--secret" } },
            { "unset", h => h.Unset(), new[] { "shelf", "SHELF_PLUGINS" } },
            { "install", h => h.Install(), new[] { "other", "--from", "github:owner/other", "--set", "SHELF_PLUGINS=/elsewhere" } },
        };

        [Theory]
        [MemberData(nameof(ChangesToTheHostsVariable))]
        public async Task HostsVariable_CannotBeChanged(string name, Func<AppHarness, ICommand> command, string[] args)
        {
            _ = name; // Names the case in the test's display name.
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                h.AddRelease("owner/other", "1.0.0", Changed(AppHarness.HostManifest, ("\"command\": \"shelf\",", string.Empty)), "bin/shelf");
                IReadOnlyList<string> before = h.Snapshot();

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => command(h).ExecuteAsync(args, CancellationToken.None));

                Assert.Equal("SHELF_PLUGINS is set by updaemon.", ex.Message);
                h.AssertUnchangedSince(before);
            }
        }

        private const string NotYetAHostManifest = """{ "executable": "bin/shelf", "command": "shelf", "service": {} }""";

        /// <summary>The app before it took extensions, with a value stored for the variable they will come in.</summary>
        private static async Task<AppHarness> WithStoredVariableAsync(bool secret)
        {
            AppHarness h = new AppHarness();
            await h.InstallHostAsync(NotYetAHostManifest);
            string[] set = ["shelf", "SHELF_PLUGINS=/elsewhere", .. secret ? new[] { "--secret" } : Array.Empty<string>()];
            Assert.Equal(0, await h.Set().ExecuteAsync(set));
            h.AddHostRelease("2.0.0");
            return h;
        }

        private static async Task<string?> StoredVariableAsync(AppHarness h) =>
            (await h.HostAsync()).Settings.GetValueOrDefault("SHELF_PLUGINS") ?? (await h.Secrets.GetAppSecretsAsync("shelf")).GetValueOrDefault("SHELF_PLUGINS");

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Update_HostStartsDeclaringAVariableWithAStoredValue_DropsTheValue(bool secret)
        {
            using (AppHarness h = await WithStoredVariableAsync(secret))
            {
                string atRestart = string.Empty;
                h.ServiceManager.OnRestart = () => atRestart = Shell.RunAsync(h.Scope.GetServiceWrapperPath("shelf")).Result;

                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));

                // Overridden already when the service starts, not only once the value is gone.
                Assert.Equal($"SHELF_PLUGINS={h.Scope.GetExtensionsDirectory("shelf")}\n", atRestart);
                Assert.Null(await StoredVariableAsync(h));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Update_HostStartsDeclaringAVariableButDoesNotStayUp_KeepsTheStoredValue(bool secret)
        {
            using (AppHarness h = await WithStoredVariableAsync(secret))
            {
                h.ServiceManager.CrashOnNextStart.Add("shelf");

                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal("SHELF_PLUGINS=/elsewhere\n", await Shell.RunAsync(h.Scope.GetServiceWrapperPath("shelf")));
                Assert.Equal("/elsewhere", await StoredVariableAsync(h));
                Assert.False(Directory.Exists(h.Scope.GetExtensionsDirectory("shelf")));
            }
        }

        /// <summary>By hand too, once the variable is one updaemon sets.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Rollback_ToAVersionDeclaringAVariableWithAStoredValue_DropsTheValue(bool secret)
        {
            using (AppHarness h = await WithStoredVariableAsync(secret))
            {
                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));
                Assert.Equal(0, await h.RollbackCommand().ExecuteAsync(new[] { "shelf" }));
                string[] set = ["shelf", "SHELF_PLUGINS=/elsewhere", .. secret ? new[] { "--secret" } : Array.Empty<string>()];
                Assert.Equal(0, await h.Set().ExecuteAsync(set));

                Assert.Equal(0, await h.RollbackCommand().ExecuteAsync(new[] { "shelf", "2.0.0" }));

                Assert.Null(await StoredVariableAsync(h));
            }
        }

        /// <summary>The wrapper overrides the value, so nothing depends on it going.</summary>
        [SkippableFact]
        public async Task Update_StoredValueCannotBeDropped_WarnsAndSucceeds()
        {
            Unwritable.SkipUnderRoot();
            using (AppHarness h = await WithStoredVariableAsync(secret: true))
            using (Unwritable.Pending unwritable = Unwritable.Prepare(h.Scope.SecretsDirectory))
            {
                h.ServiceManager.OnRestart = unwritable.Make;

                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.StartsWith("Warning: shelf: ", Assert.Single(h.Output.Errors));
                Assert.Equal($"SHELF_PLUGINS={h.Scope.GetExtensionsDirectory("shelf")}\n", await Shell.RunAsync(h.Scope.GetServiceWrapperPath("shelf")));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Update_HostStopsTakingExtensions_RemovesTheirFolderOnlyWhenItIsEmpty(bool holdsSomething)
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync();
                string something = Path.Combine(h.Scope.GetExtensionsDirectory("shelf"), "put-there-by-hand");
                if (holdsSomething)
                {
                    File.WriteAllText(something, "keep");
                }

                h.AddHostRelease("2.0.0", NotYetAHostManifest);

                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal(holdsSomething, File.Exists(something));
                Assert.Equal(holdsSomething, Directory.Exists(h.Scope.GetExtensionsDirectory("shelf")));
            }
        }

        [Fact]
        public async Task Update_HostStopsTakingExtensionsWhileSomeAreInstalled_StaysAndNamesThem()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostWithImagesAsync();
                h.AddExtensionRelease("me/video", "1.0.0");
                await h.Install().ExecuteAsync(new[] { "shelf/video", "--from", "github:me/video" });
                h.AddHostRelease("2.0.0", NotYetAHostManifest);
                h.Output.Clear();
                IReadOnlyList<string> before = h.Snapshot();

                Assert.Equal(1, await h.Update().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal(
                    new[] { "Error: shelf: 2.0.0 takes no extensions; 1.0.0 keeps running. Run 'updaemon remove shelf/images', 'updaemon remove shelf/video', then 'updaemon update shelf'." },
                    h.Output.Errors);
                h.AssertUnchangedSince(before);
            }
        }

        [Fact]
        public async Task Rollback_HostTookNoExtensionsThenAndSomeAreInstalled_StaysAndNamesThem()
        {
            using (AppHarness h = new AppHarness())
            {
                await h.InstallHostAsync(NotYetAHostManifest);
                h.AddHostRelease("2.0.0");
                Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));
                Assert.Equal(0, await h.Install().ExecuteAsync(new[] { "shelf/images" }));
                IReadOnlyList<string> before = h.Snapshot();

                InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.RollbackCommand().ExecuteAsync(new[] { "shelf" }));

                Assert.Equal("1.0.0 takes no extensions; 2.0.0 keeps running. Run 'updaemon remove shelf/images', then 'updaemon rollback shelf 1.0.0'.", ex.Message);
                h.AssertUnchangedSince(before);
            }
        }

        /// <summary>Set by the wrapper after the secrets, like the variable of the extensions.</summary>
        [Fact]
        public async Task DataDirectory_StoredAsASecretSomehow_IsOverridden()
        {
            using (AppHarness h = new AppHarness())
            {
                Assert.Equal(0, await h.InstallByteShelfAsync());
                Dictionary<string, string> secrets = await h.Secrets.GetAppSecretsAsync("byteshelf");
                secrets[AppManifest.DataDirectoryVariable] = "/elsewhere";
                await h.Secrets.SaveAppSecretsAsync("byteshelf", null, secrets);

                Assert.Contains($"{AppManifest.DataDirectoryVariable}={h.Scope.GetDataDirectory("byteshelf")}\n", await Shell.RunAsync(h.Scope.GetServiceWrapperPath("byteshelf")));
            }
        }
    }
}
