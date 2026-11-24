using Updaemon.Models;

namespace Updaemon.Tests.Helpers
{
    /// <summary>The host and the extension of it that <see cref="AppHarness.InstallHostWithImagesAsync"/> installs, and versions of them.</summary>
    public static class ShelfPackages
    {
        public const string ImagesRemote = "owner/shelf/images.zip";

        public static readonly AppName Shelf = new AppName("shelf");

        public static readonly ExtensionName Images = new ExtensionName("shelf", "images");

        public static readonly Version One = new Version(1, 0, 0);

        public static readonly Version Two = new Version(2, 0, 0);

        /// <summary>The host's manifest with everything changed that a generated file is made from.</summary>
        public static readonly string ChangedManifest = Changed(
            AppHarness.HostManifest, ("\"command\": \"shelf\"", "\"command\": \"shelf2\""), ("\"serve\"", "\"serve2\""), ("SHELF_PLUGINS", "SHELF_EXTENSIONS"));

        /// <summary>The host's manifest without its service.</summary>
        public static readonly string CommandOnlyManifest = Changed(AppHarness.HostManifest, ("\"service\": { \"args\": [\"serve\"] },", string.Empty));

        /// <summary><paramref name="text"/> with each part replaced; fails when one is not in it.</summary>
        public static string Changed(string text, params (string From, string To)[] changes)
        {
            foreach ((string from, string to) in changes)
            {
                Assert.Contains(from, text);
                text = text.Replace(from, to);
            }

            return text;
        }

        /// <summary>The host at 1.0.0 with images, and its release 2.0.0 that has no service.</summary>
        public static async Task<AppHarness> WithAReleaseWithoutTheServiceAsync()
        {
            AppHarness h = new AppHarness();
            await h.InstallHostWithImagesAsync();
            h.AddHostRelease("2.0.0", CommandOnlyManifest);
            return h;
        }

        /// <summary>The host without a service at 1.0.0 with images, and its release 2.0.0 that adds it. Restarting a service without a unit fails, as systemctl's does.</summary>
        public static async Task<AppHarness> WithAReleaseAddingTheServiceAsync()
        {
            AppHarness h = new AppHarness();
            await h.InstallHostWithImagesAsync(CommandOnlyManifest);
            h.AddHostRelease("2.0.0");
            h.ServiceManager.OnRestart = () => RequireUnit(h);
            return h;
        }

        public static void RequireUnit(AppHarness h)
        {
            if (!File.Exists(h.Scope.GetUnitFilePath("shelf")))
            {
                throw new InvalidOperationException("Unit shelf.service not found.");
            }
        }
    }
}
