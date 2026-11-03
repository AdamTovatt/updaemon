using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Updaemon.Common.Utilities
{
    /// <summary>
    /// A release's <c>SHA256SUMS</c> asset, in <c>sha256sum</c>'s format: one
    /// <c>&lt;hex&gt;  &lt;name&gt;</c> per line, or <c>&lt;hex&gt; *&lt;name&gt;</c> in binary mode.
    /// A name is matched without its directory, so a list written as <c>sha256sum dist/*</c> works.
    /// Lines in any other form are skipped: only the line for the asset being checked matters.
    /// </summary>
    public sealed partial class Sha256Sums
    {
        public const string AssetName = "SHA256SUMS";

        private readonly ILookup<string, string> _hashesByName;

        private Sha256Sums(ILookup<string, string> hashesByName)
        {
            _hashesByName = hashesByName;
        }

        public static Sha256Sums Parse(string text)
        {
            ILookup<string, string> hashesByName = text.Split('\n')
                .Select(line => SumLine().Match(line.TrimEnd('\r')))
                .Where(match => match.Success)
                .ToLookup(
                    match => Path.GetFileName(match.Groups["name"].Value),
                    match => match.Groups["hash"].Value.ToLowerInvariant(),
                    StringComparer.Ordinal);
            return new Sha256Sums(hashesByName);
        }

        /// <summary>Checks <paramref name="filePath"/> against this list's line for <paramref name="assetName"/>.</summary>
        /// <exception cref="ChecksumException">
        /// There is no well-formed line for it, lines for it disagree, or the file does not match.
        /// </exception>
        public async Task VerifyAsync(string filePath, string assetName, CancellationToken cancellationToken = default)
        {
            string[] expected = _hashesByName[assetName].Distinct().ToArray();
            if (expected.Length == 0)
            {
                throw new ChecksumException($"{AssetName} has no '<sha-256>  {assetName}' line, so {assetName} cannot be verified.");
            }

            if (expected.Length > 1)
            {
                throw new ChecksumException($"{AssetName} has lines with different SHA-256s for {assetName}, so it cannot be verified.");
            }

            if (await HashFileAsync(filePath, cancellationToken) != expected[0])
            {
                throw new ChecksumException($"{assetName} does not match its SHA-256 in {AssetName}; the download is corrupt or was tampered with.");
            }
        }

        private static async Task<string> HashFileAsync(string filePath, CancellationToken cancellationToken)
        {
            using (FileStream file = File.OpenRead(filePath))
            {
                return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
            }
        }

        [GeneratedRegex("^(?<hash>[0-9a-fA-F]{64}) [ *](?<name>.+)$")]
        private static partial Regex SumLine();
    }
}
