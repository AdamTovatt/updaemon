namespace Updaemon.Common
{
    /// <summary>
    /// The version of the protocol updaemon and its plugins speak over the pipe.
    /// A plugin reports the version it was built with in its service information,
    /// and updaemon refuses a plugin whose version differs from its own.
    /// </summary>
    public static class PluginProtocol
    {
        /// <summary>
        /// The protocol version this build speaks. Version 1 is the unversioned protocol of
        /// updaemon 0.x; version 2 passes the machine's runtime identifier to each download, and
        /// each download reports whether it was verified against a checksum.
        /// </summary>
        public const int Version = 2;

        /// <summary>
        /// The version a plugin speaks when its service information carries none.
        /// </summary>
        public const int UnversionedPluginVersion = 1;
    }
}
