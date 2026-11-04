using Updaemon.Configuration;
using Updaemon.Models;

namespace Updaemon.Tests.Helpers
{
    /// <summary>
    /// Builds installed-plugin records for tests that don't care where a plugin came from.
    /// </summary>
    public static class PluginRecords
    {
        /// <summary>
        /// A plugin installed from the registry under its own name, by default from the release of the
        /// updaemon the tests run as (<see cref="AppHarness.RunningVersion"/>).
        /// </summary>
        public static InstalledPluginInfo Installed(string alias, string path, UpdaemonVersion? release = null)
        {
            return new InstalledPluginInfo
            {
                Alias = alias,
                Path = path,
                Source = new PluginSource.Registry(alias, release ?? AppHarness.RunningVersion),
            };
        }

        /// <summary>A plugin installed from release <paramref name="tag"/> of <paramref name="owner"/>/<paramref name="repo"/> on GitHub.</summary>
        public static InstalledPluginInfo FromGitHub(string alias, string path, string owner, string repo, string tag)
        {
            return new InstalledPluginInfo
            {
                Alias = alias,
                Path = path,
                Source = new PluginSource.GitHub(owner, repo, tag),
            };
        }

        /// <summary>A plugin installed from a URL the user gave.</summary>
        public static InstalledPluginInfo FromUrl(string alias, string path)
        {
            return new InstalledPluginInfo
            {
                Alias = alias,
                Path = path,
                Source = new PluginSource.Url("https://example.com/" + alias),
            };
        }
    }
}
