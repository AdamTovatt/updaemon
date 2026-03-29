namespace Updaemon.Models
{
    /// <summary>
    /// Result of a successful deploy operation.
    /// </summary>
    public class DeployResult
    {
        /// <summary>
        /// The directory where the version was downloaded.
        /// </summary>
        public required string VersionDirectory { get; init; }

        /// <summary>
        /// The path to the detected executable within the version directory.
        /// </summary>
        public required string ExecutablePath { get; init; }

        /// <summary>
        /// The symlink path that was created/updated to point to the version directory.
        /// </summary>
        public required string SymlinkPath { get; init; }
    }
}
