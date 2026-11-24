using System.Globalization;
using System.Runtime.InteropServices;

namespace Updaemon.IO
{
    /// <summary>
    /// The few libc calls .NET has no equivalent for. Updaemon runs on Linux and macOS only.
    /// </summary>
    internal static class LibC
    {
        /// <summary>The effective user id: 0 when running as root.</summary>
        public static uint EffectiveUserId() => NativeMethods.geteuid();

        /// <summary>
        /// Renames <paramref name="oldPath"/> over <paramref name="newPath"/> in one step, as rename(2)
        /// does. Unlike File.Move it also replaces a link that points to a directory.
        /// </summary>
        /// <exception cref="IOException">The rename failed; the message says why.</exception>
        public static void Rename(string oldPath, string newPath)
        {
            if (NativeMethods.rename(oldPath, newPath) != 0)
            {
                throw new IOException(Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError()));
            }
        }

        /// <summary>
        /// The path with every link resolved, in the path's parent directories too; null when it does
        /// not exist. Two paths lead to the same file exactly when these are equal.
        /// </summary>
        public static string? RealPath(string path)
        {
            IntPtr resolved = NativeMethods.realpath(path, IntPtr.Zero);
            if (resolved == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUTF8(resolved);
            }
            finally
            {
                NativeMethods.free(resolved);
            }
        }

        public sealed record User(string Name, uint Id, string Home);

        /// <summary>The user database's entry for a name or, as <c>id</c>, <c>chown</c> and systemd read one too, a user id; null when it has none.</summary>
        public static User? FindUser(string nameOrId)
        {
            // getpwnam returns a struct passwd: two pointers, then uid at 16 on both systems. Its home
            // directory follows the gecos pointer on Linux; macOS puts pw_change and pw_class before it.
            IntPtr entry = NativeMethods.getpwnam(nameOrId);
            if (entry == IntPtr.Zero && uint.TryParse(nameOrId, NumberStyles.None, CultureInfo.InvariantCulture, out uint userId))
            {
                entry = NativeMethods.getpwuid(userId);
            }

            if (entry == IntPtr.Zero)
            {
                return null;
            }

            string? name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(entry, 0));
            uint id = (uint)Marshal.ReadInt32(entry, 16);
            string? home = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(entry, OperatingSystem.IsMacOS() ? 48 : 32));
            return string.IsNullOrEmpty(name) || string.IsNullOrEmpty(home) ? null : new User(name, id, home);
        }

        private static class NativeMethods
        {
            [DllImport("libc", SetLastError = false)]
            public static extern IntPtr getpwnam(string name);

            [DllImport("libc", SetLastError = false)]
            public static extern IntPtr getpwuid(uint id);

            [DllImport("libc", SetLastError = false)]
            public static extern uint geteuid();

            [DllImport("libc", SetLastError = true)]
            public static extern int rename(string oldPath, string newPath);

            [DllImport("libc", SetLastError = true)]
            public static extern IntPtr realpath(string path, IntPtr resolvedPath);

            [DllImport("libc", SetLastError = false)]
            public static extern void free(IntPtr pointer);
        }
    }
}
