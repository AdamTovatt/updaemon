using System.Runtime.Versioning;

// Updaemon is published only for linux-* and osx-arm64 RIDs. Telling the analyzer that the
// assembly is unsupported on Windows lets CA1416 fire only on genuine cross-platform issues
// (for example, accidentally calling a macOS-only API from a Linux-only class) instead of
// flagging every single File.SetUnixFileMode call site project-wide.
[assembly: UnsupportedOSPlatform("windows")]
