using ByteShelfClient;
using ByteShelfCommon;
using Updaemon.Common;
using Updaemon.Common.Models;
using Updaemon.Common.Utilities;

namespace Updaemon.Distribution.ByteShelfDistribution
{
    /// <summary>
    /// Distribution service for apps on a ByteShelf 2.0 shelf: each version a directory
    /// <c>/&lt;app&gt;/&lt;version&gt;/</c> (see <see cref="ByteShelfRemote"/>), read with a shelf key.
    /// </summary>
    public sealed class ByteShelfDistributionService : IDistributionService
    {
        public const string Alias = "byteshelf";

        public const string UrlSecret = "byteShelfUrl";

        public const string KeySecret = "byteShelfKey";

        private readonly IVersionParser _versionParser;
        private readonly IDownloadPostProcessor _postProcessor;
        private readonly Func<Uri, string, IByteShelfClient> _connect;
        private string? _url;
        private string? _key;
        private IByteShelfClient? _client;

        /// <param name="connect">The client for a server's address, ending in '/', and a shelf key's secret.</param>
        public ByteShelfDistributionService(
            IVersionParser versionParser,
            IDownloadPostProcessor postProcessor,
            Func<Uri, string, IByteShelfClient> connect)
        {
            _versionParser = versionParser;
            _postProcessor = postProcessor;
            _connect = connect;
        }

        public DistributionServiceInformation GetServiceInformation()
        {
            return new DistributionServiceInformation
            {
                FullName = "ByteShelf",
                DefaultAlias = Alias,
                Description = "Distribution service that uses a ByteShelf 2.0 shelf.",
                Version = typeof(ByteShelfDistributionService).Assembly.GetName().Version!.ToString(3),
                Secrets = new List<DistributionSecretInfo>
                {
                    new DistributionSecretInfo
                    {
                        Name = UrlSecret,
                        IsRequired = true,
                        Description = "The ByteShelf server's address."
                    },
                    new DistributionSecretInfo
                    {
                        Name = KeySecret,
                        IsRequired = true,
                        Description = "A shelf key for the shelf the apps are on; a read key is enough."
                    }
                }
            };
        }

        /// <remarks>
        /// Only keeps the secrets: a missing one is reported by the call that needs it, where updaemon
        /// can ask for it and call again.
        /// </remarks>
        public Task InitializeAsync(SecretCollection secrets, CancellationToken cancellationToken = default)
        {
            _url = secrets.GetValueIgnoreCase(UrlSecret);
            _key = secrets.GetValueIgnoreCase(KeySecret);
            _client = null;
            return Task.CompletedTask;
        }

        public Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            return WithVersionsAsync(
                serviceName,
                (_, _, versions) => Task.FromResult<Version?>(versions.Count == 0 ? null : versions.Max(version => version.Version)),
                cancellationToken);
        }

        public Task<DownloadResult> DownloadVersionAsync(
            string serviceName,
            Version version,
            string runtimeIdentifier,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            return WithVersionsAsync(
                serviceName,
                async (shelf, remote, versions) =>
                {
                    ShelfDirectory directory = versions.Where(candidate => candidate.Version == version).Select(candidate => candidate.Directory).ToArray() switch
                    {
                        [ShelfDirectory one] => one,
                        [] => throw new InvalidOperationException($"{remote} has no version {version} any more."),
                        ShelfDirectory[] several => throw new InvalidOperationException(
                            $"{remote} has several directories for version {version}: {string.Join(", ", several.Select(candidate => candidate.Path))}. Keep one."),
                    };

                    DirectoryListing listing = await shelf.ListAsync(directory.Id, cancellationToken);
                    ReleaseAssets<ShelfFile> assets = new ReleaseAssets<ShelfFile>(
                        $"version {directory.Name} of {remote}",
                        remote.ToString(),
                        listing.Files,
                        file => file.Name,
                        (file, token) => ReadTextAsync(shelf, file, token),
                        (file, filePath, token) => SaveAsync(shelf, file, filePath, token),
                        _postProcessor);
                    return await assets.DownloadAsync(remote.Pattern, runtimeIdentifier, targetPath, cancellationToken);
                },
                cancellationToken);
        }

        /// <summary>
        /// Reads the remote, then runs <paramref name="call"/> with the app's versions on its shelf. A
        /// refused key, on any request, is a <see cref="CredentialNeededException"/>.
        /// </summary>
        private async Task<T> WithVersionsAsync<T>(
            string serviceName,
            Func<IShelfClient, ByteShelfRemote, IReadOnlyList<VersionDirectory>, Task<T>> call,
            CancellationToken cancellationToken)
        {
            ByteShelfRemote remote = ByteShelfRemote.Parse(serviceName);
            IShelfClient shelf = OpenShelf(remote);
            try
            {
                return await call(shelf, remote, await ListVersionsAsync(shelf, remote, cancellationToken));
            }
            catch (ByteShelfException ex) when (IsKeyRefusal(ex))
            {
                throw KeyRefused(ex, remote);
            }
        }

        /// <exception cref="InvalidOperationException">The server's address is not set, or is not one.</exception>
        /// <exception cref="CredentialNeededException">The key is not set.</exception>
        private IShelfClient OpenShelf(ByteShelfRemote remote)
        {
            string setUrl = $"Set it with 'updaemon secret-set {Alias} {UrlSecret} <url>'.";
            if (string.IsNullOrWhiteSpace(_url))
            {
                throw new InvalidOperationException($"The ByteShelf server's address, {UrlSecret}, is not set. {setUrl}");
            }

            if (!Uri.TryCreate(_url.TrimEnd('/') + "/", UriKind.Absolute, out Uri? server) || (server.Scheme != Uri.UriSchemeHttps && server.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException($"The {UrlSecret} '{_url}' is not an http or https address. {setUrl}");
            }

            if (string.IsNullOrWhiteSpace(_key))
            {
                throw new CredentialNeededException(
                    new CredentialNeed { Secret = KeySecret, Problem = CredentialProblem.Missing },
                    $"The {KeySecret} is not set: reading the shelf '{remote.Shelf}' needs a shelf key for it.");
            }

            _client ??= _connect(server, _key);
            return _client.Shelf(remote.Shelf);
        }

        /// <summary>The app's version directories; directories not named as a version are left out.</summary>
        /// <exception cref="InvalidOperationException">The shelf or the app's directory was not found.</exception>
        private async Task<IReadOnlyList<VersionDirectory>> ListVersionsAsync(IShelfClient shelf, ByteShelfRemote remote, CancellationToken cancellationToken)
        {
            DirectoryListing listing;
            try
            {
                listing = await shelf.ListAsync(remote.AppDirectory, cancellationToken);
            }
            catch (ByteShelfException ex) when (ex.Code == ErrorCodes.NotFound)
            {
                throw await NotFoundAsync(shelf, remote, cancellationToken);
            }

            List<VersionDirectory> versions = new List<VersionDirectory>();
            foreach (ListedDirectory listed in listing.Directories)
            {
                Version? version = _versionParser.Parse(listed.Directory.Name);
                if (version != null)
                {
                    versions.Add(new VersionDirectory(version, listed.Directory));
                }
            }

            return versions;
        }

        /// <summary>
        /// Why the app's directory was not found. ByteShelf answers not_found both for a missing directory
        /// and for a shelf the key cannot see; only the shelf itself says which.
        /// </summary>
        private static async Task<Exception> NotFoundAsync(IShelfClient shelf, ByteShelfRemote remote, CancellationToken cancellationToken)
        {
            try
            {
                await shelf.GetInfoAsync(cancellationToken);
            }
            catch (ByteShelfException ex) when (ex.Code == ErrorCodes.NotFound)
            {
                return new InvalidOperationException(
                    $"The shelf '{remote.Shelf}' was not found: it does not exist, or the {KeySecret} cannot see it (a shelf key works on its own shelf only). " +
                    $"Remotes are <shelf>/<app> since ByteShelf 2.0. Set another key with 'updaemon secret-set {Alias} {KeySecret}'.");
            }

            return new InvalidOperationException($"{remote} has no versions: the shelf '{remote.Shelf}' has no directory {remote.AppDirectory}.");
        }

        private static bool IsKeyRefusal(ByteShelfException ex) =>
            ex.Code is ErrorCodes.Unauthenticated or ErrorCodes.InvalidCredentials or ErrorCodes.PermissionDenied;

        private static CredentialNeededException KeyRefused(ByteShelfException ex, ByteShelfRemote remote)
        {
            string message = ex.Code == ErrorCodes.PermissionDenied
                ? $"The {KeySecret} may not read the shelf '{remote.Shelf}': it needs read access."
                : $"ByteShelf rejected the {KeySecret}: it is wrong, or the key was deleted.";
            return new CredentialNeededException(new CredentialNeed { Secret = KeySecret, Problem = CredentialProblem.Rejected }, message);
        }

        private static async Task<string> ReadTextAsync(IShelfClient shelf, ShelfFile file, CancellationToken cancellationToken)
        {
            await using (Stream content = await shelf.OpenReadAsync(file.Id, cancellationToken))
            using (StreamReader reader = new StreamReader(content))
            {
                return await reader.ReadToEndAsync(cancellationToken);
            }
        }

        /// <summary>Saves the file whole: reading it to the end checks it against the SHA-256 it was stored with.</summary>
        private static async Task SaveAsync(IShelfClient shelf, ShelfFile file, string filePath, CancellationToken cancellationToken)
        {
            await using (Stream content = await shelf.OpenReadAsync(file.Id, cancellationToken))
            await using (FileStream target = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(target, cancellationToken);
            }
        }

        private sealed record VersionDirectory(Version Version, ShelfDirectory Directory);
    }
}
