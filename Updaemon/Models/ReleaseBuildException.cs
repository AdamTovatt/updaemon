namespace Updaemon.Models
{
    /// <summary>
    /// A release has no build for this machine, or several; the message lists the release's assets.
    /// </summary>
    public sealed class ReleaseBuildException : InvalidOperationException
    {
        public ReleaseBuildException(string message)
            : base(message)
        {
        }
    }
}
