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
    }
}
