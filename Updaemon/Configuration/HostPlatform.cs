using System.Runtime.InteropServices;

namespace Updaemon.Configuration
{
    /// <summary>
    /// The machine updaemon runs on: its OS family and .NET runtime identifier (for example
    /// <c>linux-arm64</c> or <c>osx-arm64</c>). Independent of the scope updaemon runs in.
    /// </summary>
    public sealed record HostPlatform(bool IsMacOS, string RuntimeIdentifier)
    {
        public static HostPlatform Current { get; } = new HostPlatform(OperatingSystem.IsMacOS(), DetectRuntimeIdentifier());

        private static string DetectRuntimeIdentifier()
        {
            string os = OperatingSystem.IsMacOS() ? "osx" : "linux";

            string arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                Architecture.X86 => "x86",
                Architecture other => other.ToString().ToLowerInvariant(),
            };

            return $"{os}-{arch}";
        }
    }
}
