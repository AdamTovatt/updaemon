using Updaemon.IO;

namespace Updaemon.Tests.Helpers
{
    /// <summary>
    /// What updaemon 0.x left in a system scope: its config, the service my-api at 1.0.0 (0.9.0 kept)
    /// with its unit, the CLI tool ripgrep at 14.0.0 linked on PATH as rg and ripgrep, and the github
    /// plugin with a secret.
    /// </summary>
    public static class ZeroXLayout
    {
        public const string PluginFileName = "Updaemon.GithubDistributionService-linux-arm64";

        /// <summary>The config's releaseRetentionCount, which differs from the default.</summary>
        public const int ReleaseRetentionCount = 3;

        public const string Settings = "Environment=API_URL=https://api.example";

        public static string PluginPath(AppHarness h) => Path.Combine(h.Scope.PluginsDirectory, "github", PluginFileName);

        public static string UnitPath(AppHarness h) => h.Scope.GetUnitFilePath("my-api");

        public static string Config(string pluginPath, bool apiRestarts = true) => $$"""
            {
              "installedPlugins": { "github": { "alias": "github", "path": "{{pluginPath}}" } },
              "services": [
                { "localName": "my-api", "remoteName": "owner/my-api", "executableName": null, "distributionPluginAlias": "github", "serviceType": "Service", "autoRestart": {{(apiRestarts ? "true" : "false")}} },
                { "localName": "ripgrep", "remoteName": "BurntSushi/ripgrep", "executableName": "rg", "distributionPluginAlias": "github", "serviceType": "Cli", "autoRestart": true }
              ],
              "releaseRetentionCount": {{ReleaseRetentionCount}}
            }
            """;

        /// <summary>my-api's unit as 0.x wrote it, with <paramref name="serviceLines"/> added to its [Service] section.</summary>
        public static string Unit(AppHarness h, string serviceLines = "") =>
            Unit("my-api", h.Scope.GetCurrentLinkPath("my-api"), "my-api", serviceLines);

        /// <summary>A unit as 0.x wrote it from its template, with <paramref name="serviceLines"/> added to its [Service] section.</summary>
        public static string Unit(string appName, string currentLink, string executable, string serviceLines = "")
        {
            return $"""
                [Unit]
                Description={appName} service managed by updaemon
                After=network.target

                [Service]
                Type=simple
                {serviceLines}
                WorkingDirectory={currentLink}
                ExecStart={currentLink}/{executable}
                Restart=always
                RestartSec=10
                StandardOutput=journal
                StandardError=journal
                SyslogIdentifier={appName}

                [Install]
                WantedBy=multi-user.target

                """;
        }

        public static void Write(AppHarness h, string serviceLines = Settings)
        {
            Directory.CreateDirectory(h.Scope.RootDirectory);
            File.WriteAllText(h.Scope.ConfigFilePath, Config(PluginPath(h)));
            File.WriteAllText(Path.Combine(h.Scope.RootDirectory, "default-unit.template"), "template");
            Directory.CreateDirectory(Path.GetDirectoryName(PluginPath(h))!);
            File.WriteAllText(PluginPath(h), "0.x plugin");
            File.WriteAllText(Path.Combine(h.Scope.PluginsDirectory, "github", "secrets.txt"), "githubToken=abc");

            Release(h, "my-api", "0.9.0", "my-api");
            Release(h, "my-api", "1.0.0", "my-api");
            File.CreateSymbolicLink(h.Scope.GetCurrentLinkPath("my-api"), Path.Combine(h.Scope.GetAppDirectory("my-api"), "1.0.0"));
            Directory.CreateDirectory(h.Scope.UnitDirectory);
            File.WriteAllText(UnitPath(h), Unit(h, serviceLines));

            Release(h, "ripgrep", "14.0.0", "rg");
            File.CreateSymbolicLink(h.Scope.GetCurrentLinkPath("ripgrep"), Path.Combine(h.Scope.GetAppDirectory("ripgrep"), "14.0.0"));
            Directory.CreateDirectory(h.Scope.BinDirectory);
            string target = Path.Combine(h.Scope.GetCurrentLinkPath("ripgrep"), "rg");
            File.CreateSymbolicLink(h.Scope.GetCommandPath("rg"), target);
            File.CreateSymbolicLink(h.Scope.GetCommandPath("ripgrep"), target);
            h.ServiceManager.ServiceRunningStates["my-api"] = true;
        }

        private static void Release(AppHarness h, string app, string version, string executable)
        {
            string path = Path.Combine(h.Scope.GetAppDirectory(app), version, executable);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "#!/bin/sh\n");
            File.SetUnixFileMode(path, AtomicFile.Executable);
        }
    }
}
