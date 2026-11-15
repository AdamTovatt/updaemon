using ByteShelfCommon;

namespace Updaemon.Distribution.ByteShelfDistribution
{
    /// <summary>
    /// Where an app's versions are: <c>&lt;shelf&gt;/&lt;app&gt;</c>, each version a directory
    /// <c>/&lt;app&gt;/&lt;version&gt;/</c> on the shelf, or <c>&lt;shelf&gt;/&lt;app&gt;/&lt;pattern&gt;</c>
    /// to name the asset instead of taking the build for this machine. Only <see cref="Parse"/> makes one,
    /// so a remote in any other form is refused before anything is sent.
    /// </summary>
    public sealed class ByteShelfRemote
    {
        private const string Form = "<shelf>/<app>, or <shelf>/<app>/<pattern> to pick the asset";

        private ByteShelfRemote(string shelf, string appDirectory, string? pattern)
        {
            Shelf = shelf;
            AppDirectory = appDirectory;
            Pattern = pattern;
        }

        public string Shelf { get; }

        /// <summary>The normalized path of the directory that holds the app's version directories.</summary>
        public string AppDirectory { get; }

        public string? Pattern { get; }

        /// <summary>Reads a remote; slashes around it are dropped, as the GitHub plugin drops them.</summary>
        /// <exception cref="ArgumentException">The remote is not in either form; the message says how.</exception>
        public static ByteShelfRemote Parse(string remote)
        {
            string[] parts = remote.Trim('/').Split('/');
            if (parts.Length == 1)
            {
                // The form ByteShelf 1.x remotes had, an app with no shelf; such an app cannot be moved over.
                throw new ArgumentException(
                    $"'{remote}' names no shelf: remotes are <shelf>/<app> since ByteShelf 2.0. " +
                    $"Run 'updaemon remove <app>', then 'updaemon install <app> --from {ByteShelfDistributionService.Alias}:<shelf>/<app>'.");
            }

            if (parts.Length > 3 || parts.Any(part => part.Length == 0))
            {
                throw new ArgumentException($"'{remote}' is not a ByteShelf remote: use {Form}.");
            }

            if (!ShelfName.TryValidate(parts[0], out string? shelfError))
            {
                throw new ArgumentException($"'{remote}' is not a ByteShelf remote ({Form}): {shelfError}");
            }

            if (!ShelfPath.TryValidateName(parts[1], out string? appError))
            {
                throw new ArgumentException($"'{remote}' is not a ByteShelf remote ({Form}): the app is not a directory name: {appError}");
            }

            return new ByteShelfRemote(parts[0], ShelfPath.Combine(ShelfPath.Root, parts[1]), parts.Length == 3 ? parts[2] : null);
        }

        /// <summary>The remote without its pattern: <c>&lt;shelf&gt;/&lt;app&gt;</c>.</summary>
        public override string ToString() => $"{Shelf}{AppDirectory}";
    }
}
