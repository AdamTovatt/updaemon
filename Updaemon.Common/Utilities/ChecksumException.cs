namespace Updaemon.Common.Utilities
{
    /// <summary>
    /// A download could not be verified against its release's <see cref="Sha256Sums"/>: there is
    /// none, it has no line for the file, or the file does not match. The message says which.
    /// </summary>
    public sealed class ChecksumException : Exception
    {
        public ChecksumException(string message)
            : base(message)
        {
        }
    }
}
