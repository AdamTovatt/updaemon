using System.Text.RegularExpressions;
using Updaemon.Common.Models;

namespace Updaemon.Common.Utilities
{
    /// <summary>
    /// The assets of one release, wherever a plugin finds them (a GitHub release, a version directory on a
    /// shelf), and the one way updaemon installs from them: pick the asset, read the release's
    /// <see cref="Sha256Sums"/> if it has one, download the asset, verify it, extract it.
    /// </summary>
    /// <typeparam name="T">How the plugin's source describes an asset.</typeparam>
    public sealed class ReleaseAssets<T>
        where T : class
    {
        private readonly string _label;
        private readonly string _remote;
        private readonly IReadOnlyList<T> _assets;
        private readonly Func<T, string> _nameOf;
        private readonly Func<T, CancellationToken, Task<string>> _readText;
        private readonly Func<T, string, CancellationToken, Task> _saveTo;
        private readonly IDownloadPostProcessor _postProcessor;

        /// <param name="label">The release as messages name it, e.g. "release v1.2.0 of owner/repo".</param>
        /// <param name="remote">The remote without a pattern, which <c>&lt;remote&gt;/&lt;pattern&gt;</c> extends to pick an asset.</param>
        /// <param name="assets">Every asset of the release.</param>
        /// <param name="nameOf">An asset's file name.</param>
        /// <param name="readText">Reads a small asset as text: the release's SHA256SUMS.</param>
        /// <param name="saveTo">Downloads an asset to the file path given.</param>
        /// <param name="postProcessor">Extracts the download once it is verified.</param>
        public ReleaseAssets(
            string label,
            string remote,
            IReadOnlyList<T> assets,
            Func<T, string> nameOf,
            Func<T, CancellationToken, Task<string>> readText,
            Func<T, string, CancellationToken, Task> saveTo,
            IDownloadPostProcessor postProcessor)
        {
            _label = label;
            _remote = remote;
            _assets = assets;
            _nameOf = nameOf;
            _readText = readText;
            _saveTo = saveTo;
            _postProcessor = postProcessor;
        }

        /// <summary>
        /// Puts the asset <see cref="Select"/> picks in <paramref name="targetPath"/> and extracts it. With a
        /// SHA256SUMS in the release, it is read first and the asset must match its line.
        /// </summary>
        /// <param name="pattern">The asset to take, from the remote; null for the build for this machine.</param>
        /// <returns>Verified when the asset was checked against the release's SHA256SUMS.</returns>
        /// <exception cref="InvalidOperationException">
        /// No one asset is picked, the SHA256SUMS has no usable line for it, or it does not match.
        /// </exception>
        public async Task<DownloadResult> DownloadAsync(string? pattern, string runtimeIdentifier, string targetPath, CancellationToken cancellationToken = default)
        {
            T asset = Select(pattern, runtimeIdentifier);
            ExpectedSha256? expected = await ReadExpectedAsync(asset, cancellationToken);

            Directory.CreateDirectory(targetPath);
            string targetFilePath = Path.Combine(targetPath, _nameOf(asset));
            await _saveTo(asset, targetFilePath, cancellationToken);
            if (expected != null)
            {
                try
                {
                    await expected.VerifyAsync(targetFilePath, cancellationToken);
                }
                catch (ChecksumException ex)
                {
                    throw NotInstalled(ex);
                }
            }

            // Extracts a .zip or .tar.gz; only a verified (or unverifiable) download gets this far.
            await _postProcessor.ProcessAsync(targetPath, cancellationToken);
            return new DownloadResult { Verified = expected != null };
        }

        /// <summary>
        /// What the asset must match, from the release's SHA256SUMS, read before the download. Null when
        /// the release has none; throws when it has one without a line for the asset.
        /// </summary>
        private async Task<ExpectedSha256?> ReadExpectedAsync(T asset, CancellationToken cancellationToken)
        {
            T? sums = _assets.FirstOrDefault(candidate => _nameOf(candidate) == Sha256Sums.AssetName);
            if (sums == null)
            {
                return null;
            }

            string text = await _readText(sums, cancellationToken);
            try
            {
                return Sha256Sums.Parse(text).For(_nameOf(asset));
            }
            catch (ChecksumException ex)
            {
                throw NotInstalled(ex);
            }
        }

        private InvalidOperationException NotInstalled(ChecksumException ex) =>
            new InvalidOperationException($"The {_label} was not installed: {ex.Message}");

        /// <summary>
        /// The one asset to download. With a pattern, that is the asset matching it (wildcards like
        /// *.zip); without one, the build for this machine, named *-&lt;rid&gt; with one of
        /// <see cref="DownloadPostProcessor.ArchiveEndings"/> or none. The release's SHA256SUMS is never
        /// one. Anything but exactly one match fails with the release's asset names, one per line, and
        /// how to pick one.
        /// </summary>
        private T Select(string? pattern, string runtimeIdentifier)
        {
            if (_assets.Count == 0)
            {
                throw new InvalidOperationException($"The {_label} has no assets.");
            }

            string[] buildEndings = BuildEndings(runtimeIdentifier);
            Func<string, bool> matches = pattern == null
                ? name => buildEndings.Any(ending => name.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
                : WildcardMatcher(pattern);
            T[] matchingAssets = _assets.Where(asset => _nameOf(asset) != Sha256Sums.AssetName && matches(_nameOf(asset))).ToArray();
            if (matchingAssets.Length == 1)
            {
                return matchingAssets[0];
            }

            string[] buildNames = buildEndings.Select(ending => $"*{ending}").ToArray();
            string buildName = $"{string.Join(", ", buildNames[..^1])} or {buildNames[^1]}";
            string problem = (pattern, matchingAssets.Length) switch
            {
                (null, 0) => $"No asset in the {_label} is named {buildName}, the build for this machine.",
                (null, _) => $"Several assets in the {_label} are named {buildName}, the build for this machine.",
                (_, 0) => $"No asset in the {_label} matches '{pattern}'.",
                _ => $"Several assets in the {_label} match '{pattern}'.",
            };

            throw new InvalidOperationException(
                $"{problem} Its assets:\n" +
                string.Join("\n", _assets.Select(asset => $"  {_nameOf(asset)}")) + "\n" +
                $"Pick one with the remote {_remote}/<pattern>, where <pattern> is an asset name and * matches any text.");
        }

        /// <summary>How the build for <paramref name="runtimeIdentifier"/> may end: each archive, then a bare binary.</summary>
        private static string[] BuildEndings(string runtimeIdentifier)
        {
            return [.. DownloadPostProcessor.ArchiveEndings.Select(ending => $"-{runtimeIdentifier}{ending}"), $"-{runtimeIdentifier}"];
        }

        private static Func<string, bool> WildcardMatcher(string pattern)
        {
            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            Regex regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
            return regex.IsMatch;
        }
    }
}
