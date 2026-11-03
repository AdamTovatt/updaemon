using Updaemon.Configuration;

namespace Updaemon.Tests.Helpers
{
    /// <summary>
    /// Builds an <see cref="UpdaemonScope"/> whose every directory lives under a temp root.
    /// </summary>
    public static class TestScope
    {
        public static UpdaemonScope Create(string root, bool isSystem = false, bool isMacOS = false)
        {
            return new UpdaemonScope
            {
                IsSystem = isSystem,
                Platform = new HostPlatform(isMacOS, isMacOS ? "osx-arm64" : "linux-arm64"),
                RootDirectory = Path.Combine(root, "root"),
                AppsDirectory = Path.Combine(root, "apps"),
                BinDirectory = Path.Combine(root, "bin"),
                UnitDirectory = Path.Combine(root, "units"),
                LogDirectory = Path.Combine(root, "logs"),
                UserId = isSystem ? 0u : 1000u,
            };
        }
    }
}
