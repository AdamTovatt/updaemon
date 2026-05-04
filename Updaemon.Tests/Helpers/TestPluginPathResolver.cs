namespace Updaemon.Tests.Helpers
{
    /// <summary>
    /// Locates the Updaemon.TestPlugin apphost binary on disk so integration
    /// tests can spawn it via Process.Start. The plugin is built alongside the
    /// test assembly via a project reference, so its output sits next to the
    /// test project's output under a sibling solution-root directory.
    /// </summary>
    public static class TestPluginPathResolver
    {
        public static string Resolve()
        {
            string testAssemblyDir = AppContext.BaseDirectory;
            DirectoryInfo runtimeDir = new DirectoryInfo(testAssemblyDir);
            DirectoryInfo? configDir = runtimeDir.Parent;
            DirectoryInfo? binDir = configDir?.Parent;
            DirectoryInfo? testProjectDir = binDir?.Parent;
            DirectoryInfo? solutionRoot = testProjectDir?.Parent;

            if (solutionRoot == null || configDir == null)
            {
                throw new InvalidOperationException(
                    $"Unable to locate solution root from test base directory: {testAssemblyDir}");
            }

            string pluginPath = Path.Combine(
                solutionRoot.FullName,
                "Updaemon.TestPlugin",
                "bin",
                configDir.Name,
                runtimeDir.Name,
                "Updaemon.TestPlugin");

            if (!File.Exists(pluginPath))
            {
                throw new FileNotFoundException(
                    $"Test plugin not found at '{pluginPath}'. Ensure Updaemon.TestPlugin builds before running these tests.");
            }

            return pluginPath;
        }
    }
}
