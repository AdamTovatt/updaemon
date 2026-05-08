namespace Updaemon.Configuration
{
    /// <summary>
    /// Bundles the OS-dependent settings a <see cref="Updaemon.Services.UnitFileManager"/> needs:
    /// which embedded template to load, where the on-disk default lives, where unit files are
    /// written, and what extension / label-prefix they take.
    /// </summary>
    public sealed record UnitFilePlatform(
        string EmbeddedResourceName,
        string TemplateFileName,
        string UnitFileDirectory,
        string UnitFileExtension,
        string UnitLabelPrefix)
    {
        /// <summary>Returns the platform descriptor for the running OS.</summary>
        public static UnitFilePlatform ForCurrentOS() =>
            OperatingSystem.IsMacOS() ? Launchd : Systemd;

        /// <summary>The systemd unit-file profile, useful for tests.</summary>
        public static UnitFilePlatform Systemd { get; } = new(
            EmbeddedResourceName: "Updaemon.Templates.service.template",
            TemplateFileName: "default-unit.template",
            UnitFileDirectory: "/etc/systemd/system",
            UnitFileExtension: ".service",
            UnitLabelPrefix: string.Empty);

        /// <summary>The launchd plist profile, useful for tests.</summary>
        public static UnitFilePlatform Launchd { get; } = new(
            EmbeddedResourceName: "Updaemon.Templates.launchd.plist.template",
            TemplateFileName: "default-plist.template",
            UnitFileDirectory: "/Library/LaunchDaemons",
            UnitFileExtension: ".plist",
            UnitLabelPrefix: "com.updaemon.");
    }
}
