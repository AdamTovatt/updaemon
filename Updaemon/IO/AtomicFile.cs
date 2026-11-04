using System.Text;

namespace Updaemon.IO
{
    /// <summary>
    /// Writes generated files so readers never see a partial file or a looser mode than intended.
    /// </summary>
    public static class AtomicFile
    {
        public const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        public const UnixFileMode Readable = OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

        public const UnixFileMode Executable = Readable | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

        /// <summary>
        /// Writes <paramref name="content"/> to a temporary file created with <paramref name="mode"/>, then
        /// renames it over <paramref name="path"/>.
        /// </summary>
        public static Task WriteAsync(string path, string content, UnixFileMode mode, CancellationToken cancellationToken = default) =>
            WriteAsync(path, content, mode, beforeReplace: null, cancellationToken);

        /// <summary>
        /// As above, with <paramref name="beforeReplace"/> run on the temporary file before the rename, for
        /// what must already hold when the file appears at <paramref name="path"/>, such as its owner.
        /// </summary>
        public static async Task WriteAsync(string path, string content, UnixFileMode mode, Func<string, Task>? beforeReplace, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

            FileStreamOptions options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                UnixCreateMode = mode,
            };

            try
            {
                using (StreamWriter writer = new StreamWriter(temporaryPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), options))
                {
                    await writer.WriteAsync(content.AsMemory(), cancellationToken);
                }

                // The umask may have cleared bits from the create mode; set it exactly.
                File.SetUnixFileMode(temporaryPath, mode);
                if (beforeReplace != null)
                {
                    await beforeReplace(temporaryPath);
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
            catch
            {
                File.Delete(temporaryPath);
                throw;
            }
        }
    }
}
