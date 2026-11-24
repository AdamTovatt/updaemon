using System.Text.RegularExpressions;
using Updaemon.Models;
using Updaemon.Tests.Helpers;
using static Updaemon.Tests.Helpers.ShelfPackages;

namespace Updaemon.Tests.Services
{
    /// <summary>
    /// Every change to an installed app, killed at each of its steps: around each save of the entry, after each
    /// link and file, around each save of the secrets, and before what is done to the service. The next change leaves it as it was before or as it is once done,
    /// and the service running what is there.
    /// </summary>
    public class KilledChangeTests
    {
        private static Task InstalledAsync(AppHarness h) => InstalledAsync(h, AppHarness.HostManifest);

        private static async Task InstalledAsync(AppHarness h, string manifest)
        {
            await h.InstallHostWithImagesAsync(manifest);
            Assert.Equal(0, await h.Set().ExecuteAsync(new[] { "shelf", "KEPT=1", "TOKEN=old" }));
        }

        private static Task ReleasedAsync(AppHarness h) => ReleasedAsync(h, AppHarness.HostManifest, ChangedManifest);

        private static async Task ReleasedAsync(AppHarness h, string installed, string released)
        {
            await InstalledAsync(h, installed);
            h.AddHostRelease("2.0.0", released);
            h.AddExtensionRelease(ImagesRemote, "2.0.0");
        }

        private static async Task UpdatedAsync(AppHarness h)
        {
            await ReleasedAsync(h);
            Assert.Equal(0, await h.Update().ExecuteAsync(new[] { "shelf" }));
        }

        private static async Task CrashingAsync(AppHarness h)
        {
            await ReleasedAsync(h);
            h.ServiceManager.CrashOnNextStart.Add("shelf");
        }

        private static async Task WithASecretAsync(AppHarness h)
        {
            await InstalledAsync(h);
            Assert.Equal(0, await h.Set().ExecuteAsync(new[] { "shelf", "HIDDEN=1", "--secret" }));
        }

        public static TheoryData<string, Func<AppHarness, Task>, Func<AppHarness, Task<int>>, int, bool> Changes => new TheoryData<string, Func<AppHarness, Task>, Func<AppHarness, Task<int>>, int, bool>
        {
            { "update", ReleasedAsync, h => h.Update().ExecuteAsync(new[] { "shelf" }), 0, false },
            { "update that goes back", CrashingAsync, h => h.Update().ExecuteAsync(new[] { "shelf" }), 1, false },
            {
                "update that cannot be recorded",
                async h =>
                {
                    await ReleasedAsync(h);
                    h.SavedConfig.FailNextWithNothingLeft();
                },
                h => h.Update().ExecuteAsync(new[] { "shelf" }),
                1,
                false
            },
            { "update that adds the service", h => ReleasedAsync(h, CommandOnlyManifest, ChangedManifest), h => h.Update().ExecuteAsync(new[] { "shelf" }), 0, false },
            {
                "update that adds the service and goes back",
                async h =>
                {
                    await ReleasedAsync(h, CommandOnlyManifest, ChangedManifest);
                    h.ServiceManager.CrashOnNextStart.Add("shelf");
                },
                h => h.Update().ExecuteAsync(new[] { "shelf" }),
                1,
                false
            },
            { "update that removes the service", h => ReleasedAsync(h, AppHarness.HostManifest, CommandOnlyManifest), h => h.Update().ExecuteAsync(new[] { "shelf" }), 0, false },
            {
                "update that removes the service and cannot be recorded",
                async h =>
                {
                    await ReleasedAsync(h, AppHarness.HostManifest, CommandOnlyManifest);
                    h.SavedConfig.FailNextWithNothingLeft();
                },
                h => h.Update().ExecuteAsync(new[] { "shelf" }),
                1,
                false
            },
            { "update of an extension", ReleasedAsync, h => h.Update().ExecuteAsync(new[] { "shelf/images" }), 0, false },
            { "rollback", UpdatedAsync, h => h.RollbackCommand().ExecuteAsync(new[] { "shelf" }), 0, false },
            { "rollback of an extension", UpdatedAsync, h => h.RollbackCommand().ExecuteAsync(new[] { "shelf/images" }), 0, false },
            { "install", h => h.InstallHostAsync(), h => h.Install().ExecuteAsync(new[] { "shelf/images" }), 0, false },
            {
                "install that goes back",
                async h =>
                {
                    await h.InstallHostAsync();
                    h.ServiceManager.CrashOnNextStart.Add("shelf");
                },
                h => h.Install().ExecuteAsync(new[] { "shelf/images" }),
                1,
                false
            },
            { "remove", InstalledAsync, h => h.Remove().ExecuteAsync(new[] { "shelf/images" }), 0, false },
            { "set", InstalledAsync, h => h.Set().ExecuteAsync(new[] { "shelf", "NEW=2" }), 0, false },
            { "set of a secret", InstalledAsync, h => h.Set().ExecuteAsync(new[] { "shelf", "TOKEN=new", "--secret" }), 0, true },
            { "unset of a secret", WithASecretAsync, h => h.Unset().ExecuteAsync(new[] { "shelf", "HIDDEN" }), 0, true },
            { "unset", InstalledAsync, h => h.Unset().ExecuteAsync(new[] { "shelf", "KEPT" }), 0, false },
            { "set-args", InstalledAsync, h => h.SetArgs().ExecuteAsync(new[] { "shelf", "--", "other" }), 0, false },
        };

        /// <param name="secretsFirst">Whether the secrets, which are not in the entry, may be as once it is done while the entry is as before.</param>
        [Theory]
        [MemberData(nameof(Changes))]
        public async Task KilledAtAnyStep_TheNextChangePutsItRight(string name, Func<AppHarness, Task> arrange, Func<AppHarness, Task<int>> change, int exitCode, bool secretsFirst)
        {
            IReadOnlyList<string> done;
            int steps = 0;
            using (AppHarness h = new AppHarness())
            {
                await arrange(h);
                h.OnStep = () => steps++;
                Assert.Equal(exitCode, await change(h));
                done = State(h);
            }

            Assert.True(steps >= 4, $"{name} takes {steps} steps.");
            bool stood = false;
            for (int step = 1; step <= steps; step++)
            {
                using (AppHarness h = new AppHarness())
                {
                    await arrange(h);
                    IReadOnlyList<string> before = State(h);
                    IReadOnlyList<string> running = Loaded(h);
                    int taken = 0;
                    h.OnStep = () =>
                    {
                        if (++taken == step)
                        {
                            h.KeepForCutOff();
                        }
                    };
                    h.ServiceManager.OnStarted = () => running = taken < step ? Loaded(h) : running;
                    await change(h);
                    h.CutOff();
                    h.ServiceManager.OnStarted = () => running = Loaded(h);

                    Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                    string killed = $"{name}, killed at step {step} of {steps}";
                    IReadOnlyList<string> after = State(h);
                    bool isDone = after.SequenceEqual(done);
                    Assert.True(
                        isDone || after.SequenceEqual(before) || (secretsFirst && Entry(after) == Entry(before)),
                        $"{killed}, unlike before:\n{AppHarness.Difference(after, before)}\nunlike once done:\n{AppHarness.Difference(after, done)}");
                    Assert.True(isDone || !stood, $"{killed}, is put back, though it stood when killed at an earlier step.");
                    stood = isDone && !done.SequenceEqual(before);
                    AssertRunsWhatIsThere(h, running, killed);
                    Assert.Null((await h.HostAsync()).Unfinished);
                }
            }
        }

        public static TheoryData<string, Func<AppHarness, Task>, Func<AppHarness, Task<int>>, Action<AppHarness>> ChangesToPutRight =>
            new TheoryData<string, Func<AppHarness, Task>, Func<AppHarness, Task<int>>, Action<AppHarness>>
            {
                { "update", ReleasedAsync, h => h.Update().ExecuteAsync(new[] { "shelf" }), AtTheRestart },
                { "update that adds the service", h => ReleasedAsync(h, CommandOnlyManifest, ChangedManifest), h => h.Update().ExecuteAsync(new[] { "shelf" }), AtTheRestart },
                { "update that removes the service", h => ReleasedAsync(h, AppHarness.HostManifest, CommandOnlyManifest), h => h.Update().ExecuteAsync(new[] { "shelf" }), AtTheReload },
                { "install", h => h.InstallHostAsync(), h => h.Install().ExecuteAsync(new[] { "shelf/images" }), AtTheRestart },
                { "rollback", UpdatedAsync, h => h.RollbackCommand().ExecuteAsync(new[] { "shelf" }), AtTheRestart },
                { "remove", InstalledAsync, h => h.Remove().ExecuteAsync(new[] { "shelf/images" }), AtTheRestart },
                { "set", InstalledAsync, h => h.Set().ExecuteAsync(new[] { "shelf", "NEW=2" }), OnceSaved },
                { "set of a secret", InstalledAsync, h => h.Set().ExecuteAsync(new[] { "shelf", "TOKEN=new", "--secret" }), OnceSaved },
            };

        private static void AtTheRestart(AppHarness h) => h.ServiceManager.OnRestart = h.KeepForCutOff;

        private static void AtTheReload(AppHarness h) => h.ServiceManager.OnDaemonReload = h.KeepForCutOff;

        /// <summary>The entry is as the change made it; no file followed yet.</summary>
        private static void OnceSaved(AppHarness h) => h.SavedConfig.OnSaved = app =>
        {
            if (!app.Settings.ContainsKey("TOKEN") || app.Settings.ContainsKey("NEW"))
            {
                h.KeepForCutOff();
            }
        };

        /// <summary>The same, when the run that puts it right is killed too: the one after that gets as far as one that was not.</summary>
        [Theory]
        [MemberData(nameof(ChangesToPutRight))]
        public async Task KilledWhileItIsPutRight_TheChangeAfterThatPutsItRight(string name, Func<AppHarness, Task> arrange, Func<AppHarness, Task<int>> change, Action<AppHarness> killedAt)
        {
            IReadOnlyList<string> putRight;
            using (AppHarness h = await KilledAsync(arrange, change, killedAt))
            {
                Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));
                putRight = State(h);
            }

            for (int step = 1; ; step++)
            {
                using (AppHarness h = await KilledAsync(arrange, change, killedAt))
                {
                    IReadOnlyList<string> running = Array.Empty<string>();
                    int taken = 0;
                    h.OnStep = () =>
                    {
                        if (++taken == step)
                        {
                            h.KeepForCutOff();
                        }
                    };
                    h.ServiceManager.OnStarted = () => running = taken < step ? Loaded(h) : running;
                    Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));
                    if (taken < step)
                    {
                        Assert.True(step > 3, $"{name} is put right in {taken} steps.");
                        return;
                    }

                    h.CutOff();
                    h.ServiceManager.OnStarted = () => running = Loaded(h);
                    Assert.Equal(0, await h.Unset().ExecuteAsync(new[] { "shelf", "NOTHING" }));

                    string killed = $"{name}, killed at step {step} of putting it right";
                    Assert.True(State(h).SequenceEqual(putRight), $"{killed}, unlike when it is not:\n{AppHarness.Difference(State(h), putRight)}");
                    AssertRunsWhatIsThere(h, running, killed);
                    Assert.Null((await h.HostAsync()).Unfinished);
                }
            }
        }

        private static async Task<AppHarness> KilledAsync(Func<AppHarness, Task> arrange, Func<AppHarness, Task<int>> change, Action<AppHarness> killedAt)
        {
            AppHarness h = new AppHarness();
            await arrange(h);
            killedAt(h);
            await change(h);
            h.CutOff();
            return h;
        }

        private static void AssertRunsWhatIsThere(AppHarness h, IReadOnlyList<string> running, string killed) =>
            Assert.True(!h.Deployer.HasService("shelf") || running.SequenceEqual(Loaded(h)), $"{killed}, the service runs on something else:\n{AppHarness.Difference(Loaded(h), running)}");

        private static string Entry(IReadOnlyList<string> state) => Assert.Single(state, entry => entry.Contains("config.json"));

        /// <summary>
        /// What the service is like, and all there is on disk but the versions, which a run may download again, and
        /// the directory of units, which is everyone's.
        /// </summary>
        private static IReadOnlyList<string> State(AppHarness h) => h.Snapshot()
            .Select(entry => entry.Replace(h.Root, string.Empty))
            .Where(entry => entry != "/units/" && !Regex.IsMatch(entry, @"^/apps/shelf/(\d|extension-versions)"))
            .Append(!h.Deployer.HasService("shelf")
                ? "no service"
                : $"service enabled: {h.ServiceManager.EnabledServices.Contains("shelf")}, running: {h.ServiceManager.ServiceRunningStates.GetValueOrDefault("shelf")}")
            .ToList();

        /// <summary>What the service reads when it starts.</summary>
        private static IReadOnlyList<string> Loaded(AppHarness h) => State(h)
            .Where(entry => Regex.IsMatch(entry, @"^/(units/|apps/shelf/(current|run|extensions/)|root/(settings|secrets)/)"))
            .ToList();
    }
}
