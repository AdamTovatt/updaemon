namespace Updaemon.Common.Rpc
{
    /// <summary>
    /// Parameters of the DownloadVersionAsync call, as sent over the pipe.
    /// </summary>
    public class DownloadVersionParameters
    {
        public required string ServiceName { get; init; }

        public required string Version { get; init; }

        /// <summary>
        /// The runtime identifier of the machine updaemon runs on, e.g. "linux-arm64".
        /// </summary>
        public required string RuntimeIdentifier { get; init; }

        public required string TargetPath { get; init; }
    }
}
