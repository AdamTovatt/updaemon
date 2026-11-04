using Updaemon.IO;

namespace Updaemon.Configuration
{
    /// <summary>
    /// Every path and service-manager address updaemon uses, for one scope. The scope is decided by
    /// who runs updaemon: root gets the system scope, anyone else gets their own user scope.
    /// </summary>
    public sealed record UpdaemonScope
    {
        private const string MacLabelPrefix = "com.updaemon.";

        /// <summary>True for the machine-wide scope (run as root); false for a single user's scope.</summary>
        public required bool IsSystem { get; init; }

        public required HostPlatform Platform { get; init; }

        /// <summary>Holds the config, lock, plugins, settings, secrets and data directories.</summary>
        public required string RootDirectory { get; init; }

        /// <summary>Holds one directory per app, each with its version directories and a <c>current</c> link.</summary>
        public required string AppsDirectory { get; init; }

        /// <summary>Where commands are linked so they are on PATH.</summary>
        public required string BinDirectory { get; init; }

        /// <summary>Where systemd units or launchd plists are written.</summary>
        public required string UnitDirectory { get; init; }

        /// <summary>Where launchd writes service output. Unused on Linux, where services log to the journal.</summary>
        public required string LogDirectory { get; init; }

        /// <summary>The effective user id updaemon runs as; addresses the per-user launchd domain.</summary>
        public required uint UserId { get; init; }

        /// <summary>
        /// Who runs updaemon, by name: the scope's own user in a user scope; in the system scope, the
        /// user who ran sudo, or null when root runs it without sudo.
        /// </summary>
        public required string? UserName { get; init; }

        public string Name => IsSystem ? "system" : "user";

        /// <summary>Whose apps this scope holds, as a sentence names them: "the whole machine" or "user adam".</summary>
        public string Owner => IsSystem ? "the whole machine" : $"user {UserName}";

        /// <summary>The opening line of every command that changes state: this scope, and how to get the other.</summary>
        public string Announcement => IsSystem
            ? $"For the whole machine (run without sudo for {(UserName == null ? "your own user" : $"user {UserName}")} only)."
            : $"For user {UserName} only (run with sudo for the whole machine).";

        public string ConfigFilePath => Path.Combine(RootDirectory, "config.json");

        public string LockFilePath => Path.Combine(RootDirectory, "updaemon.lock");

        public string PluginsDirectory => Path.Combine(RootDirectory, "plugins");

        /// <summary>What converting an updaemon 0.x setup replaced: its config, units, template and timer.</summary>
        public string ZeroXBackupDirectory => Path.Combine(RootDirectory, "0.x-backup");

        /// <summary>The <c>updaemon</c> command: a link to one of the versions in <see cref="UpdaemonVersionsDirectory"/>.</summary>
        public string UpdaemonCommandPath => Path.Combine(BinDirectory, "updaemon");

        /// <summary>Updaemon's own versions, one directory each, installed by self-update and install.sh.</summary>
        public string UpdaemonVersionsDirectory => Path.Combine(RootDirectory, "versions");

        public string GetUpdaemonBinaryPath(UpdaemonVersion release) => Path.Combine(UpdaemonVersionsDirectory, release.ToString(), "updaemon");

        /// <summary>The directories a state-changing command writes to.</summary>
        public IReadOnlyList<string> WritableDirectories => new[] { RootDirectory, AppsDirectory, BinDirectory, UnitDirectory };

        public string SecretsDirectory => Path.Combine(RootDirectory, "secrets");

        public string GetAppDirectory(string appName) => Path.Combine(AppsDirectory, appName);

        /// <summary>The generated environment file with the app's non-secret settings.</summary>
        public string GetSettingsFilePath(string appName) => Path.Combine(RootDirectory, "settings", appName + ".env");

        /// <summary>The app's secret settings, readable only by the user the app runs as.</summary>
        public string GetSecretsFilePath(string appName) => Path.Combine(SecretsDirectory, appName + ".env");

        /// <summary>The app's own data, kept across updates and rollbacks.</summary>
        public string GetDataDirectory(string appName) => Path.Combine(RootDirectory, "data", appName);

        /// <summary>The generated command wrapper on PATH.</summary>
        public string GetCommandPath(string commandName) => Path.Combine(BinDirectory, commandName);

        public string GetCurrentLinkPath(string appName) => Path.Combine(AppsDirectory, appName, "current");

        /// <summary>The wrapper a service's unit or plist starts, beside its versions.</summary>
        public string GetServiceWrapperPath(string appName) => Path.Combine(AppsDirectory, appName, "run");

        /// <summary>macOS only: where launchd writes the service's standard output.</summary>
        public string GetOutputLogPath(string appName) => Path.Combine(LogDirectory, appName + ".log");

        /// <summary>macOS only: where launchd writes the service's standard error.</summary>
        public string GetErrorLogPath(string appName) => Path.Combine(LogDirectory, appName + ".err.log");

        /// <summary>macOS only: where launchd writes the update timer's runs, output and errors alike.</summary>
        public string UpdateLogPath => Path.Combine(LogDirectory, "updaemon.log");

        /// <summary>The systemd unit name or launchd label of an app's service.</summary>
        public string GetServiceLabel(string appName) => Platform.IsMacOS ? MacLabelPrefix + appName : appName;

        public string GetUnitFilePath(string appName) =>
            Path.Combine(UnitDirectory, GetServiceLabel(appName) + (Platform.IsMacOS ? ".plist" : ".service"));

        /// <summary>
        /// The systemd unit name (without suffix) or launchd label of the update timer. No app label can
        /// equal it: the app name "updaemon" is reserved, and on macOS every app label has a suffix after the prefix.
        /// </summary>
        public string TimerLabel => Platform.IsMacOS ? MacLabelPrefix.TrimEnd('.') : "updaemon";

        /// <summary>The launchd domain services are bootstrapped into.</summary>
        public string LaunchdDomain => IsSystem ? "system" : $"gui/{UserId}";

        /// <summary>Arguments that address this scope's systemd instance, put before every systemctl or journalctl verb.</summary>
        public string[] SystemdScopeArguments => IsSystem ? Array.Empty<string>() : new[] { "--user" };

        public static UpdaemonScope Detect()
        {
            uint userId = LibC.EffectiveUserId();
            if (userId == 0)
            {
                string? sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
                return ForSystem(HostPlatform.Current, string.IsNullOrEmpty(sudoUser) || sudoUser == "root" ? null : sudoUser);
            }

            string home = Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return ForUser(
                HostPlatform.Current,
                userId,
                Environment.UserName,
                home,
                Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));
        }

        /// <summary>
        /// The scope this run is not in: the system scope from a user's, or under sudo, the scope of the
        /// user who ran sudo. That user's XDG variables are not known here, so their default paths are
        /// used. Null when there is no such user (root without sudo, or a name the user database lacks).
        /// </summary>
        public static UpdaemonScope? DetectOther(UpdaemonScope scope)
        {
            if (!scope.IsSystem)
            {
                return ForSystem(scope.Platform, scope.UserName);
            }

            LibC.User? user = scope.UserName == null ? null : LibC.FindUser(scope.UserName);
            return user == null ? null : ForUser(scope.Platform, user.Id, scope.UserName!, user.Home, null, null);
        }

        /// <param name="sudoUser">The user who ran sudo, if any.</param>
        public static UpdaemonScope ForSystem(HostPlatform platform, string? sudoUser)
        {
            return new UpdaemonScope
            {
                IsSystem = true,
                Platform = platform,
                UserName = sudoUser,
                RootDirectory = platform.IsMacOS ? "/usr/local/var/updaemon" : "/var/lib/updaemon",
                AppsDirectory = platform.IsMacOS ? "/usr/local/opt" : "/opt",
                BinDirectory = "/usr/local/bin",
                UnitDirectory = platform.IsMacOS ? "/Library/LaunchDaemons" : "/etc/systemd/system",
                LogDirectory = "/var/log/updaemon",
                UserId = 0,
            };
        }

        public static UpdaemonScope ForUser(HostPlatform platform, uint userId, string userName, string home, string? xdgDataHome, string? xdgConfigHome)
        {
            string root;
            string unitDirectory;
            if (platform.IsMacOS)
            {
                root = Path.Combine(home, "Library", "Application Support", "updaemon");
                unitDirectory = Path.Combine(home, "Library", "LaunchAgents");
            }
            else
            {
                // XDG says a relative value is invalid and must be ignored.
                string dataHome = IsAbsolute(xdgDataHome) ? xdgDataHome! : Path.Combine(home, ".local", "share");
                string configHome = IsAbsolute(xdgConfigHome) ? xdgConfigHome! : Path.Combine(home, ".config");
                root = Path.Combine(dataHome, "updaemon");
                unitDirectory = Path.Combine(configHome, "systemd", "user");
            }

            return new UpdaemonScope
            {
                IsSystem = false,
                Platform = platform,
                UserName = userName,
                RootDirectory = root,
                AppsDirectory = Path.Combine(root, "apps"),
                BinDirectory = Path.Combine(home, ".local", "bin"),
                UnitDirectory = unitDirectory,
                LogDirectory = Path.Combine(home, "Library", "Logs", "updaemon"),
                UserId = userId,
            };
        }

        private static bool IsAbsolute(string? path) => !string.IsNullOrEmpty(path) && Path.IsPathRooted(path);
    }
}
