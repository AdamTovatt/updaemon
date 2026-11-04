using System.Security.Cryptography;

namespace Updaemon.Common.Utilities
{
    /// <summary>
    /// The SHA-256 a release's <c>SHA256SUMS</c> gives for one asset. Only <see cref="Sha256Sums"/> makes one,
    /// so a download is checked against a list read before it was fetched.
    /// </summary>
    public sealed class ExpectedSha256
    {
        private readonly string _hash;

        internal ExpectedSha256(string assetName, string hash)
        {
            AssetName = assetName;
            _hash = hash;
        }

        public string AssetName { get; }

        /// <exception cref="ChecksumException">The file at <paramref name="filePath"/> does not match.</exception>
        public async Task VerifyAsync(string filePath, CancellationToken cancellationToken = default)
        {
            string actual;
            using (FileStream file = File.OpenRead(filePath))
            {
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
            }

            if (actual != _hash)
            {
                throw new ChecksumException($"{AssetName} does not match its SHA-256 in {Sha256Sums.AssetName}; the download is corrupt or was tampered with.");
            }
        }
    }
}
