using System.Formats.Tar;
using System.IO.Compression;

namespace Updaemon.Common.Utilities
{
    /// <summary>
    /// Extracts a downloaded archive (one of <see cref="ArchiveEndings"/>) in place, then unwraps a single top-level
    /// directory. Any other download is left as it is.
    /// </summary>
    public class DownloadPostProcessor : IDownloadPostProcessor
    {
        private delegate Task Extractor(string archive, string targetDirectory, CancellationToken cancellationToken);

        // Both refuse an entry whose path, or link target, leads outside the target directory.
        private static readonly (string Ending, Extractor Extract)[] Archives =
        [
            (".zip", ExtractZipAsync),
            (".tar.gz", ExtractTarGzAsync),
        ];

        /// <summary>The file name endings of the archives that get extracted.</summary>
        public static IReadOnlyList<string> ArchiveEndings { get; } = Archives.Select(archive => archive.Ending).ToArray();

        /// <exception cref="InvalidOperationException">
        /// The archive could not be extracted: it is corrupt, or an entry would land outside the
        /// target directory.
        /// </exception>
        public async Task ProcessAsync(string targetDirectory, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(targetDirectory))
            {
                return;
            }

            string[] files = Directory.GetFiles(targetDirectory);
            if (files.Length != 1 || Directory.GetDirectories(targetDirectory).Length != 0)
            {
                return;
            }

            string archive = files[0];
            Extractor? extract = ExtractorFor(archive);
            if (extract == null)
            {
                return;
            }

            try
            {
                await extract(archive, targetDirectory, cancellationToken);
                File.Delete(archive);
                UnwrapSingleDirectory(targetDirectory);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"{Path.GetFileName(archive)} could not be extracted: {ex.Message}", ex);
            }
        }

        private static Extractor? ExtractorFor(string path)
        {
            string name = Path.GetFileName(path);
            return Archives.FirstOrDefault(archive => name.EndsWith(archive.Ending, StringComparison.OrdinalIgnoreCase)).Extract;
        }

        private static Task ExtractZipAsync(string archive, string targetDirectory, CancellationToken cancellationToken)
        {
            return Task.Run(() => ZipFile.ExtractToDirectory(archive, targetDirectory), cancellationToken);
        }

        /// <summary>Extracts with each entry's file mode, so executables stay executable.</summary>
        private static async Task ExtractTarGzAsync(string archive, string targetDirectory, CancellationToken cancellationToken)
        {
            await using (FileStream file = File.OpenRead(archive))
            await using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                await TarFile.ExtractToDirectoryAsync(gzip, targetDirectory, overwriteFiles: false, cancellationToken);
            }
        }

        /// <summary>Moves the contents of a lone top-level directory up into <paramref name="targetDirectory"/>.</summary>
        private static void UnwrapSingleDirectory(string targetDirectory)
        {
            if (Directory.GetFiles(targetDirectory).Length != 0 || Directory.GetDirectories(targetDirectory) is not [string lone])
            {
                return;
            }

            // Renamed first, so an entry with the directory's own name (app/app) has room.
            string single = Path.Combine(targetDirectory, $".unwrap-{Guid.NewGuid():N}");
            Directory.Move(lone, single);
            foreach (string entry in Directory.GetFileSystemEntries(single))
            {
                string destination = Path.Combine(targetDirectory, Path.GetFileName(entry));
                if (Directory.Exists(entry) && new DirectoryInfo(entry).LinkTarget == null)
                {
                    Directory.Move(entry, destination);
                }
                else
                {
                    File.Move(entry, destination);
                }
            }

            Directory.Delete(single);
        }
    }
}
