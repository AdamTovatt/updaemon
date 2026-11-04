namespace Updaemon.Common.Models
{
    /// <summary>
    /// What a plugin's download call reports back besides putting the version in place.
    /// </summary>
    public sealed class DownloadResult
    {
        /// <summary>
        /// Whether the downloaded file was checked against a checksum its release published. False
        /// when there was none to check against; a mismatch fails the download instead.
        /// </summary>
        public required bool Verified { get; init; }
    }
}
