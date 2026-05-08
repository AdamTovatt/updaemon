using System.Runtime.Versioning;

// Tests run on the same platforms as updaemon itself (linux + macos), never Windows.
// Mirrors the assembly-level attribute on Updaemon so CA1416 doesn't fire on every
// File.SetUnixFileMode / launchctl-related test call site.
[assembly: UnsupportedOSPlatform("windows")]
