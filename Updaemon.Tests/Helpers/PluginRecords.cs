using Updaemon.Models;

namespace Updaemon.Tests.Helpers
{
    /// <summary>
    /// Builds installed-plugin records for tests that don't care where a plugin came from.
    /// </summary>
    public static class PluginRecords
    {
        /// <summary>
        /// A plugin installed from the registry under its own name.
        /// </summary>
        public static InstalledPluginInfo Installed(string alias, string path)
        {
            return new InstalledPluginInfo
            {
                Alias = alias,
                Path = path,
                Source = new PluginSource.Registry(alias),
            };
        }
    }
}
