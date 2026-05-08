namespace Updaemon.Configuration
{
    /// <summary>
    /// Provides OS-aware default paths used throughout updaemon.
    /// Linux uses systemd conventions; macOS uses launchd conventions.
    ///
    /// Unit-file related values (directory, extension, label prefix, embedded template name)
    /// live on <see cref="UnitFilePlatform"/>; this class forwards to the appropriate
    /// per-OS profile so there's one source of truth.
    /// </summary>
    public static class PlatformPaths
    {
        public static bool IsMacOS => OperatingSystem.IsMacOS();

        /// <summary>The unit-file platform descriptor for the running OS.</summary>
        public static UnitFilePlatform UnitFileProfile =>
            IsMacOS ? UnitFilePlatform.Launchd : UnitFilePlatform.Systemd;

        /// <summary>
        /// Where updaemon stores its config, plugins, and templates.
        /// Linux: /var/lib/updaemon. macOS: /usr/local/var/updaemon.
        /// </summary>
        public static string ConfigDirectory =>
            IsMacOS ? "/usr/local/var/updaemon" : "/var/lib/updaemon";

        /// <summary>
        /// Base directory for managed application versions and symlinks.
        /// Linux: /opt. macOS: /usr/local/opt.
        /// </summary>
        public static string ServicesBaseDirectory =>
            IsMacOS ? "/usr/local/opt" : "/opt";

        /// <summary>Directory where service unit files are written.</summary>
        public static string UnitFileDirectory => UnitFileProfile.UnitFileDirectory;

        /// <summary>Extension for service unit files.</summary>
        public static string UnitFileExtension => UnitFileProfile.UnitFileExtension;

        /// <summary>Prefix prepended to a service name to form its unit-file label.</summary>
        public static string UnitLabelPrefix => UnitFileProfile.UnitLabelPrefix;

        /// <summary>Where CLI tools are symlinked. /usr/local/bin on both OSes.</summary>
        public static string BinDirectory => "/usr/local/bin";

        /// <summary>Path where the updaemon binary itself is installed.</summary>
        public static string UpdaemonExecutablePath => "/usr/local/bin/updaemon";

        /// <summary>
        /// Filename (without extension) of the periodic update timer unit.
        /// Linux: updaemon. macOS: com.updaemon.timer (reverse-DNS for LaunchDaemons).
        /// </summary>
        public static string TimerUnitName =>
            IsMacOS ? "com.updaemon.timer" : "updaemon";

        /// <summary>Builds the unit-file label for a service. Equivalent to <c>UnitLabelPrefix + serviceName</c>.</summary>
        public static string GetUnitLabel(string serviceName) => UnitLabelPrefix + serviceName;

        /// <summary>Embedded resource name for the unit-file template appropriate to the current OS.</summary>
        public static string EmbeddedTemplateResourceName => UnitFileProfile.EmbeddedResourceName;

        /// <summary>Filename of the on-disk default template copy under ConfigDirectory.</summary>
        public static string TemplateFileName => UnitFileProfile.TemplateFileName;

        /// <summary>
        /// Where launchd writes service stdout/stderr logs. macOS-only — Linux services log to journald.
        /// </summary>
        public static string MacLogDirectory => "/var/log/updaemon";
    }
}
